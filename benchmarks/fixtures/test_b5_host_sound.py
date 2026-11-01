"""Unit tests for x86 ATS host buffered brake-sound diagnostic log format."""
from __future__ import annotations
import sys
import unittest
from pathlib import Path

sys.path.insert(0,str(Path(__file__).resolve().parent.parent))
from VerifyB5HostSoundBuffer import expected_events, validate_host_sound_log


def make_log(frames:int, cycle:int, profile:bool, capacity:int=4096)->str:
    all_events=expected_events(frames,cycle)
    lines=[
        f"BC_SOUND frame={frame} index={index} state={state} time={tick} "
        "bc=0 mr=0 er=0 bp=0 sap=0 speed=0"
        for frame,index,state,tick in all_events[:capacity]]
    lines.append(
        f"BC_SOUND_BUFFER_SUMMARY stored={min(len(all_events),capacity)} "
        f"dropped={max(0,len(all_events)-capacity)} capacity={capacity}")
    if profile:
        lines.append("HOST_FIRST_ELAPSE frame=1 qpc=12345 qpc_freq=10000000")
    lines.append("HOST_FINISHED")
    return "\n".join(lines)+"\n"


class HostBrakeSoundLogTests(unittest.TestCase):
    def test_cyclic_sound_keeps_frame_index_state_time(self):
        data=validate_host_sound_log(make_log(3000,7,True),3000,7,True)
        self.assertEqual((data["stored"],data["dropped"],data["events"]),(446,0,446))

    def test_overflow_is_explicit(self):
        data=validate_host_sound_log(make_log(5000,1,True),5000,1,True)
        self.assertEqual((data["stored"],data["dropped"],data["events"]),(4096,921,5017))

    def test_mismatched_state_is_rejected(self):
        invalid=make_log(3000,7,True).replace(
            "BC_SOUND frame=8 index=150 state=1 time=112",
            "BC_SOUND frame=8 index=150 state=0 time=112",1)
        with self.assertRaisesRegex(ValueError,"event order"):
            validate_host_sound_log(invalid,3000,7,True)

    def test_profile_off_has_no_sync_marker(self):
        self.assertEqual(validate_host_sound_log(make_log(100,10,False),100,10,False)["stored"],27)
        with self.assertRaisesRegex(ValueError,"Unexpected ATS"):
            validate_host_sound_log(make_log(100,10,True),100,10,False)


if __name__=="__main__":
    unittest.main()