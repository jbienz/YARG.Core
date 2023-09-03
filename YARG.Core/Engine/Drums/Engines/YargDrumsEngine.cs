using YARG.Core.Chart;
using YARG.Core.Input;

namespace YARG.Core.Engine.Drums.Engines
{
    public class YargDrumsEngine : DrumsEngine
    {
        public YargDrumsEngine(InstrumentDifficulty<DrumNote> chart, SyncTrack syncTrack, DrumsEngineParameters engineParameters) : base(chart, syncTrack, engineParameters)
        {
        }

        public override void UpdateBot(double songTime)
        {
            base.UpdateBot(songTime);

            // Skip if there are no notes left
            if (State.NoteIndex >= Notes.Count)
            {
                return;
            }

            var note = Notes[State.NoteIndex];

            bool updateToSongTime = true;
            while (note is not null && songTime >= note.Time)
            {
                updateToSongTime = false;

                // Make sure to hit each note in the "chord" individually
                bool hit = true;
                foreach (var chordNote in note.ChordEnumerator())
                {
                    State.PadHitThisUpdate = chordNote.Pad;

                    if (!UpdateHitLogic(chordNote.Time))
                    {
                        hit = false;
                    }
                }

                if (!hit) break;

                note = note.NextNote;
            }

            State.PadHitThisUpdate = -1;

            if (updateToSongTime)
            {
                UpdateHitLogic(songTime);
            }
        }

        protected override bool UpdateHitLogic(double time)
        {
            UpdateTimeVariables(time);

            DepleteStarPower(GetUsedStarPower());

            // Get the pad hit this update
            if (IsInputUpdate && CurrentInput.Button)
            {
                State.PadHitThisUpdate = CurrentInput.GetAction<DrumsAction>() switch
                {
                    DrumsAction.Kick => (int) FourLaneDrumPad.Kick,

                    DrumsAction.Drum1 => (int) FourLaneDrumPad.RedDrum,
                    DrumsAction.Drum2 => (int) FourLaneDrumPad.YellowDrum,
                    DrumsAction.Drum3 => (int) FourLaneDrumPad.BlueDrum,
                    DrumsAction.Drum4 => (int) FourLaneDrumPad.GreenDrum,

                    DrumsAction.Cymbal1 => (int) FourLaneDrumPad.YellowCymbal,
                    DrumsAction.Cymbal2 => (int) FourLaneDrumPad.BlueCymbal,
                    DrumsAction.Cymbal3 => (int) FourLaneDrumPad.GreenCymbal,

                    _ => -1
                };
            }
            else if (!IsBotUpdate)
            {
                State.PadHitThisUpdate = -1;
            }

            // Quits early if there are no notes left
            if (State.NoteIndex >= Notes.Count)
            {
                return false;
            }

            var note = Notes[State.NoteIndex];

            // Miss notes (back end)
            if (State.CurrentTime > note.Time + EngineParameters.BackEnd)
            {
                foreach (var chordNote in note.ChordEnumerator())
                {
                    if (chordNote.WasHit || chordNote.WasMissed)
                    {
                        continue;
                    }

                    // Check for activation notes that weren't hit, and auto-hit them.
                    // This may seem weird, but it prevents issues from arising when scoring
                    // activation notes.
                    if (chordNote.IsStarPowerActivator && EngineStats.CanStarPowerActivate)
                    {
                        HitNote(chordNote, true);
                        continue;
                    }

                    MissNote(chordNote);
                }

                return true;
            }

            bool isNoteHit = CheckForNoteHit();

            // Check for over hits
            if (State.PadHitThisUpdate != -1)
            {
                if (!isNoteHit)
                {
                    Overhit();
                }

                OnPadHit?.Invoke(CurrentInput.GetAction<DrumsAction>(), isNoteHit);
            }

            return isNoteHit;
        }

        protected override bool CheckForNoteHit()
        {
            var note = Notes[State.NoteIndex];
            return CheckForNoteHit(note);
        }

        protected bool CheckForNoteHit(DrumNote note)
        {
            if (State.CurrentTime < note.Time + EngineParameters.FrontEnd)
            {
                return false;
            }

            // Remember that while playing drums, all notes of a chord don't have to be hit.
            // Treat all notes separately.
            foreach (var chordNote in note.ChordEnumerator())
            {
                if (chordNote.WasHit || chordNote.WasMissed)
                {
                    continue;
                }

                if (CanNoteBeHit(chordNote))
                {
                    HitNote(chordNote);
                    return true;
                }
            }

            // If that fails, attempt to hit any of the other notes ahead of this one (in the hit window)
            // This helps a lot with combo regain, especially with fast double bass
            // Please note that this is recursive, so a loop is not required
            if (note.NextNote is not null && CheckForNoteHit(note.NextNote))
            {
                return true;
            }

            return false;
        }

        protected override bool CanNoteBeHit(DrumNote note)
        {
            return note.Pad == State.PadHitThisUpdate;
        }
    }
}