using AiOn.People;
using UnityEngine;

namespace AiOn.Player
{
    /// <summary>The player's visible body (third person): plays the person animation that matches the controller.</summary>
    public class PlayerBody : MonoBehaviour
    {
        public PlayerController player;
        public PersonAnimator person;
        [Tooltip("What the player carries in hand ('water' plays the carry walk).")]
        public string carrying;

        void LateUpdate()
        {
            if (!player || !person || !person.isActiveAndEnabled) return;
            if (!player.Grounded && Mathf.Abs(player.VerticalSpeed) > 1.5f) person.SetAnim("air");
            else if (player.Crouching) person.SetAnim("crouch");
            else if (carrying == "water" && player.HorizontalSpeed > 0.15f) person.SetAnim("carry", player.HorizontalSpeed);
            else person.Move(player.HorizontalSpeed);
        }
    }
}
