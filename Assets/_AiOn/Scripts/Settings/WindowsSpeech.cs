using System;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AiOn.Settings
{
    /// <summary>
    /// 저시력 mode's spoken prompts (the original's speechSynthesis, ko-KR): on Windows one hidden PowerShell process with
    /// System.Speech reads each line it is sent, cancelling the one before (speechSynthesis.cancel()). It picks a Korean
    /// voice when one is installed. Elsewhere, or when it cannot start, it stays silent (the original: "no voice").
    /// </summary>
    public sealed class WindowsSpeech : IDisposable
    {
        const string Script =
            "Add-Type -AssemblyName System.Speech;" +
            "$s=New-Object System.Speech.Synthesis.SpeechSynthesizer;" +
            "$v=$s.GetInstalledVoices()|Where-Object{$_.Enabled -and $_.VoiceInfo.Culture.Name -eq 'ko-KR'}|Select-Object -First 1;" +
            "if($v){$s.SelectVoice($v.VoiceInfo.Name)};" +
            "while(($l=[Console]::In.ReadLine()) -ne $null){" +
            "$s.SpeakAsyncCancelAll();" +
            "if($l.Length -gt 0){$s.SpeakAsync([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($l)))|Out-Null}}";

        Process _p;
        bool _failed;

        public static bool Supported =>
            Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;

        public void Speak(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || _failed || !Supported) return;
            try
            {
                if (_p == null || _p.HasExited) Start();
                if (_p == null) return;
                _p.StandardInput.WriteLine(SettingsMath.SpeechLine(text));
                _p.StandardInput.Flush();
            }
            catch (Exception e) { _failed = true; Debug.LogWarning($"[Speech] no voice: {e.Message}"); Dispose(); }
        }

        /// <summary>Stops what is being read (a blank line cancels).</summary>
        public void Cancel()
        {
            try { if (_p != null && !_p.HasExited) { _p.StandardInput.WriteLine(""); _p.StandardInput.Flush(); } }
            catch { /* gone */ }
        }

        void Start()
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -Command \"" + Script.Replace("\"", "\\\"") + "\"",
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            };
            _p = Process.Start(psi);
        }

        public void Dispose()
        {
            if (_p == null) return;
            try { if (!_p.HasExited) { _p.StandardInput.Close(); if (!_p.WaitForExit(300)) _p.Kill(); } }
            catch { /* already gone */ }
            _p.Dispose();
            _p = null;
        }
    }
}
