import json, sys
src = sys.argv[1]
LOCO = [("walk","m_walk_neutral_02","f_walk_neutral_02"),("brisk","m_walk_fast_02","f_walk_fast_02"),("run","m_run_neutral","f_run_neutral"),
("slow","m_walk_slow_01","f_walk_slow_01"),("carry","m_walk_neutral_02","f_walk_neutral_02")]
POSE = [("idle","m_idle_neutral_01","f_idle_neutral_01"),("talk","m_gestic_talk_neutral_01","f_gestic_talk_neutral_01"),
("phone","m_cell_phone_talk_01","f_cell_phone_textmessage"),("wave","m_wave_01","f_wave_01"),
("point","m_gestic_presentation_right_01","f_gestic_presentation_right_01"),("sitChair","m_sit_chair_idle_neutral_01","f_sit_chair_idle_neutral_01"),
("crouch","m_crouch_idle","f_crouch_idle"),("knock","m_knock_door","f_knock_door")]
arm = {"m":"m_hold_bag_idle.max.fbx","f":"f_hold_bag_idle_01.max.fbx"}
for g, col in (("m",1),("f",2)):
    json.dump({"src":src,"simplify":1.0,"fps":30,"clips":[[t[0], t[col]+".max.fbx","xy",arm[g] if t[0]=="carry" else None] for t in LOCO]}, open(f"spec_{g}_loco.json","w"))
    json.dump({"src":src,"simplify":1.0,"fps":15,"max_frames":int(sys.argv[2]),"clips":[[t[0], t[col]+".max.fbx","static",None] for t in POSE]}, open(f"spec_{g}_pose.json","w"))
