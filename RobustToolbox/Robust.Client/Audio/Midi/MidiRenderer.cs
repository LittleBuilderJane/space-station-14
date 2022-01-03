using System;
using NFluidsynth;
using Robust.Client.Graphics;
using Robust.Shared.Asynchronous;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.ViewVariables;
using MidiEvent = NFluidsynth.MidiEvent;

namespace Robust.Client.Audio.Midi
{
    public enum MidiRendererStatus : byte
    {
        None,
        Input,
        File,
    }

    public interface IMidiRenderer : IDisposable
    {

        /// <summary>
        ///     The buffered audio source of this renderer.
        /// </summary>
        internal IClydeBufferedAudioSource Source { get; }

        /// <summary>
        ///     Whether this renderer has been disposed or not.
        /// </summary>
        bool Disposed { get; }

        /// <summary>
        ///     This controls whether the midi file being played will loop or not.
        /// </summary>
        bool LoopMidi { get; set; }

        /// <summary>
        ///     This increases all note on velocities to 127.
        /// </summary>
        bool VolumeBoost { get; set; }

        /// <summary>
        ///     The midi program (instrument) the renderer is using.
        /// </summary>
        byte MidiProgram { get; set; }

        /// <summary>
        ///     The instrument bank the renderer is using.
        /// </summary>
        byte MidiBank { get; set; }

        /// <summary>
        ///     The soundfont currently selected by the renderer.
        /// </summary>
        uint MidiSoundfont { get; set; }

        /// <summary>
        ///     The current status of the renderer.
        ///     "None" if the renderer isn't playing from input or a midi file.
        ///     "Input" if the renderer is playing from midi input.
        ///     "File" if the renderer is playing from a midi file.
        /// </summary>
        MidiRendererStatus Status { get; }

        /// <summary>
        ///     Whether the sound will play in stereo or mono.
        /// </summary>
        bool Mono { get; set; }

        /// <summary>
        ///     Whether to drop messages on the percussion channel.
        /// </summary>
        bool DisablePercussionChannel { get; set; }

        /// <summary>
        /// Whether to drop messages for program change events.
        /// </summary>
        bool DisableProgramChangeEvent { get; set; }

        /// <summary>
        ///     Gets the total number of ticks possible for the MIDI player.
        /// </summary>
        int PlayerTotalTick { get; }

        /// <summary>
        ///     Gets or sets (seeks) the current tick of the MIDI player.
        /// </summary>
        int PlayerTick { get; set; }

        /// <summary>
        ///     Gets the current tick of the sequencer.
        /// </summary>
        uint SequencerTick { get; }

        /// <summary>
        ///     Gets the Time Scale of the sequencer in ticks per second. Default is 1000 for 1 tick per millisecond.
        /// </summary>
        double SequencerTimeScale { get; }

        /// <summary>
        ///     Start listening for midi input.
        /// </summary>
        bool OpenInput();

        /// <summary>
        ///     Start playing a midi file.
        /// </summary>
        /// <param name="buffer">Bytes of the midi file</param>
        bool OpenMidi(ReadOnlySpan<byte> buffer);

        /// <summary>
        ///     Stops listening for midi input.
        /// </summary>
        bool CloseInput();

        /// <summary>
        ///     Stops playing midi files.
        /// </summary>
        bool CloseMidi();

        /// <summary>
        ///     Stops all notes being played currently.
        /// </summary>
        void StopAllNotes();

        /// <summary>
        ///     Render and play MIDI to the audio source.
        /// </summary>
        internal void Render();

        /// <summary>
        ///     Loads a new soundfont into the renderer.
        /// </summary>
        void LoadSoundfont(string filename, bool resetPresets = false);

        /// <summary>
        ///     Invoked whenever a new midi event is registered.
        /// </summary>
        event Action<Shared.Audio.Midi.MidiEvent> OnMidiEvent;

        /// <summary>
        ///     Invoked when the midi player finishes playing a song.
        /// </summary>
        event Action OnMidiPlayerFinished;

        /// <summary>
        ///     The entity whose position will be used for positional audio.
        ///     This is only used if <see cref="Mono"/> is set to True.
        /// </summary>
        EntityUid? TrackingEntity { get; set; }

        /// <summary>
        ///     The position that will be used for positional audio.
        ///     This is only used if <see cref="Mono"/> is set to True
        ///     and <see cref="TrackingEntity"/> is null.
        /// </summary>
        EntityCoordinates? TrackingCoordinates { get; set; }

        /// <summary>
        ///     Send a midi event for the renderer to play.
        /// </summary>
        /// <param name="midiEvent">The midi event to be played</param>
        void SendMidiEvent(Shared.Audio.Midi.MidiEvent midiEvent);

        /// <summary>
        ///     Schedule a MIDI event to be played at a later time.
        /// </summary>
        /// <remarks>Does NOT raise a <see cref="OnMidiEvent"/> as the event is sent directly to the <see cref="Synth"/> using a <see cref="Sequencer"/>.</remarks>
        /// <param name="midiEvent">the midi event in question</param>
        /// <param name="time"></param>
        /// <param name="absolute"></param>
        void ScheduleMidiEvent(Shared.Audio.Midi.MidiEvent midiEvent, uint time, bool absolute);

        /// <summary>
        ///     Actually disposes of this renderer. Do NOT use outside the MIDI thread.
        /// </summary>
        internal void InternalDispose();
    }

    internal class MidiRenderer : IMidiRenderer
    {
        [Dependency] private readonly IClydeAudio _clydeAudio = default!;
        [Dependency] private readonly ITaskManager _taskManager = default!;
        [Dependency] private readonly ILogManager _logger = default!;

        private const int MidiSizeLimit = 2000000;
        private const double BytesToMegabytes = 0.000001d;

        private readonly ISawmill _midiSawmill;

        private Settings _settings;

        // Kept around to avoid the loader callbacks getting GC'd
        // ReSharper disable once NotAccessedField.Local
        private readonly SoundFontLoader _soundFontLoader;
        private Synth _synth;
        private Sequencer _sequencer;
        private NFluidsynth.Player? _player;
        private MidiDriver? _driver;
        private byte _midiProgram = 1;
        private byte _midiBank = 1;
        private uint _midiSoundfont = 0;
        private bool _loopMidi = false;
        private const int SampleRate = 44100;
        private const int Buffers = SampleRate / 2205;
        private readonly object _playerStateLock = new();
        private bool _debugEvents = false;
        private SequencerClientId _synthRegister;
        private SequencerClientId _debugRegister;
        public IClydeBufferedAudioSource Source { get; set; }
        IClydeBufferedAudioSource IMidiRenderer.Source => Source;

        [ViewVariables]
        public bool Disposed { get; private set; } = false;

        [ViewVariables(VVAccess.ReadWrite)]
        public byte MidiProgram
        {
            get => _midiProgram;
            set
            {
                lock (_playerStateLock)
                    for (var i = 0; i < _synth.MidiChannelCount; i++)
                        _synth.ProgramChange(i, value);

                _midiProgram = value;
            }
        }

        [ViewVariables(VVAccess.ReadWrite)]
        public byte MidiBank
        {
            get => _midiBank;
            set
            {
                lock (_playerStateLock)
                    for (var i = 0; i < _synth.MidiChannelCount; i++)
                        _synth.BankSelect(i, value);

                _midiBank = value;
            }
        }

        [ViewVariables(VVAccess.ReadWrite)]
        public uint MidiSoundfont
        {
            get => _midiSoundfont;
            set
            {
                lock (_playerStateLock)
                    for (var i = 0; i < _synth.MidiChannelCount; i++)
                        _synth.SoundFontSelect(i, value);

                _midiSoundfont = value;
            }
        }

        [ViewVariables(VVAccess.ReadWrite)]
        public bool DisablePercussionChannel { get; set; } = true;

        [ViewVariables(VVAccess.ReadWrite)]
        public bool DisableProgramChangeEvent { get; set; } = true;

        [ViewVariables(VVAccess.ReadWrite)]
        public int PlayerTotalTick => _player?.GetTotalTicks ?? 0;

        [ViewVariables(VVAccess.ReadWrite)]
        public int PlayerTick
        {
            get => _player?.CurrentTick ?? 0;
            set
            {
                lock (_playerStateLock)
                    _player?.Seek(Math.Max(Math.Min(value, PlayerTotalTick), 0));
            }
        }

        [ViewVariables(VVAccess.ReadWrite)]
        public uint SequencerTick => _sequencer?.Tick ?? 0;

        [ViewVariables(VVAccess.ReadWrite)]
        public double SequencerTimeScale => _sequencer?.TimeScale ?? 0;

        [ViewVariables(VVAccess.ReadWrite)]
        public bool Mono { get; set; }

        [ViewVariables]
        public MidiRendererStatus Status { get; private set; } = MidiRendererStatus.None;

        [ViewVariables(VVAccess.ReadWrite)]
        public bool LoopMidi
        {
            get => _loopMidi;
            set
            {
                lock (_playerStateLock)
                    _player?.SetLoop(value ? -1 : 0);
                _loopMidi = value;
            }
        }

        [ViewVariables(VVAccess.ReadWrite)]
        public bool VolumeBoost { get; set; }

        [ViewVariables(VVAccess.ReadWrite)]
        public EntityUid? TrackingEntity { get; set; } = null;

        [ViewVariables(VVAccess.ReadWrite)]
        public EntityCoordinates? TrackingCoordinates { get; set; } = null;

        internal MidiRenderer(Settings settings, SoundFontLoader soundFontLoader, bool mono = true)
        {
            IoCManager.InjectDependencies(this);
            _midiSawmill = _logger.GetSawmill("midi");
            _midiSawmill.Level = LogLevel.Info;
            Source = _clydeAudio.CreateBufferedAudioSource(Buffers, true);
            Source.SampleRate = SampleRate;
            _settings = settings;
            _soundFontLoader = soundFontLoader;
            _synth = new Synth(_settings);
            _sequencer = new Sequencer(false);
            _debugRegister = _sequencer.RegisterClient("honk", DumpSequencerEvent);
            _synthRegister = _sequencer.RegisterFluidsynth(_synth);

            _synth.AddSoundFontLoader(soundFontLoader);

            Mono = mono;
            Source.EmptyBuffers();
            Source.StartPlaying();
        }

        private void DumpSequencerEvent(uint time, SequencerEvent @event)
        {
            // ReSharper disable once UseStringInterpolation
            _midiSawmill.Debug(string.Format(
                "{0:D8}: {1} chan:{2:D2} key:{3:D5} bank:{4:D2} ctrl:{5:D5} dur:{6:D5} pitch:{7:D5} prog:{8:D3} val:{9:D5} vel:{10:D5}",
                time,
                @event.Type.ToString().PadLeft(22),
                @event.Channel,
                @event.Key,
                @event.Bank,
                @event.Control,
                @event.Duration,
                @event.Pitch,
                @event.Program,
                @event.Value,
                @event.Velocity));

            @event.Dest = _synthRegister;
            _sequencer.SendNow(@event);
        }

        public bool OpenInput()
        {
            if (Disposed)
                return false;

            if (Status != MidiRendererStatus.File) CloseMidi();
            Status = MidiRendererStatus.Input;
            StopAllNotes();

            lock (_playerStateLock)
            {
                _driver = new MidiDriver(_settings, MidiDriverEventHandler);
            }

            return true;
        }

        public bool OpenMidi(ReadOnlySpan<byte> buffer)
        {
            if (Disposed)
                return false;

            if (Status == MidiRendererStatus.Input) CloseInput();
            Status = MidiRendererStatus.File;
            StopAllNotes();

            if (buffer.Length > MidiSizeLimit)
            {
                _midiSawmill.Error("Midi file selected is too big! It was {0} MB but it should be less than {1} MB.",
                    buffer.Length * BytesToMegabytes, MidiSizeLimit * BytesToMegabytes);
                CloseMidi();
                return false;
            }

            lock (_playerStateLock)
            {
                _player?.Dispose();
                _player = new NFluidsynth.Player(_synth);
                _player.AddMem(buffer);
                _player.SetPlaybackCallback(MidiPlayerEventHandler);
                _player.Play();
                _player.SetLoop(LoopMidi ? -1 : 1);
            }

            return true;
        }

        public bool CloseInput()
        {
            if (Status != MidiRendererStatus.Input) return false;
            Status = MidiRendererStatus.None;

            lock (_playerStateLock)
            {
                _driver?.Dispose();
                _driver = null;
            }

            StopAllNotes();
            return true;
        }

        public bool CloseMidi()
        {
            if (Status != MidiRendererStatus.File) return false;
            Status = MidiRendererStatus.None;
            lock (_playerStateLock)
            {
                if (_player == null) return false;
                _player?.Stop();
                _player?.Join();
                _player?.Dispose();
                _player = null;
            }

            StopAllNotes();
            return true;
        }

        public void StopAllNotes()
        {
            lock(_playerStateLock)
                _synth.AllNotesOff(-1);
        }

        public void LoadSoundfont(string filename, bool resetPresets = false)
        {
            lock (_playerStateLock)
            {
                _synth.LoadSoundFont(filename, resetPresets);
                MidiSoundfont = 1;
            }
        }

        public event Action<Shared.Audio.Midi.MidiEvent>? OnMidiEvent;
        public event Action? OnMidiPlayerFinished;

        void IMidiRenderer.Render()
        {
            Render();
        }

        private void Render(int length = SampleRate / 250)
        {
            if (Disposed) return;

            var buffersProcessed = Source.GetNumberOfBuffersProcessed();
            if(buffersProcessed == Buffers) _midiSawmill.Warning("MIDI buffer overflow!");
            if (buffersProcessed == 0) return;

            var bufferLength = length * 2;

            unsafe
            {
                Span<int> buffers = stackalloc int[buffersProcessed];
                Span<float> audio = stackalloc float[bufferLength * buffers.Length];

                Source.GetBuffersProcessed(buffers);

                lock (_playerStateLock)
                {
                    // _sequencer.Process(10);
                    _synth?.WriteSampleFloat(length * buffers.Length, audio, 0, Mono ? 1 : 2,
                        audio, Mono ? length * buffers.Length : 1, Mono ? 1 : 2);
                }
                if (Mono) // Turn audio to mono
                {
                    var l = length * buffers.Length;

                    NumericsHelpers.Add(audio[..l], audio[l..]);
                }

                for (var i = 0; i < buffers.Length; i++)
                {
                    var buffer = buffers[i];
                    Source.WriteBuffer(buffer, audio.Slice(i * length, bufferLength));
                }

                Source.QueueBuffers(buffers);
            }

            lock (_playerStateLock)
            {
                if (Status == MidiRendererStatus.File && _player?.Status == FluidPlayerStatus.Done)
                {
                    _taskManager.RunOnMainThread(() => OnMidiPlayerFinished?.Invoke());
                    CloseMidi();
                }
            }

            if (!Source.IsPlaying) Source.StartPlaying();
        }

        private int MidiPlayerEventHandler(MidiEvent midiEvent)
        {
            if (Disposed || Status != MidiRendererStatus.File && _player?.Status == FluidPlayerStatus.Playing) return 0;
            var timestamp = SequencerTick;
            var midiEv = (Shared.Audio.Midi.MidiEvent) midiEvent;
            midiEv.Tick = timestamp;
            midiEvent.Dispose();
            SendMidiEvent(midiEv);
            return 0;
        }

        private int MidiDriverEventHandler(MidiEvent midiEvent)
        {
            if (Disposed || Status != MidiRendererStatus.Input) return 0;
            var timestamp = SequencerTick;
            var midiEv = (Shared.Audio.Midi.MidiEvent) midiEvent;
            midiEv.Tick = timestamp;
            midiEvent.Dispose();
            SendMidiEvent(midiEv);
            return 0;
        }

        public void SendMidiEvent(Shared.Audio.Midi.MidiEvent midiEvent)
        {
            if (Disposed) return;

            if (DisablePercussionChannel && midiEvent.Channel == 9)
                return;

            try
            {
                lock(_playerStateLock)
                    switch (midiEvent.Type)
                    {
                        // Note Off - 0x80
                        case 128:
                            _synth.NoteOff(midiEvent.Channel, midiEvent.Key);
                            break;

                        // Note On 0x90
                        case 144:
                            if (VolumeBoost)
                                midiEvent.Velocity = 127;
                            _synth.NoteOn(midiEvent.Channel, midiEvent.Key, midiEvent.Velocity);
                            break;

                        // After Touch - 0xA
                        case 160:
                            _synth.KeyPressure(midiEvent.Channel, midiEvent.Key, midiEvent.Value);
                            break;

                        // Control Change - 0xB0
                        case 176:
                            _synth.CC(midiEvent.Channel, midiEvent.Control, midiEvent.Value);
                            break;

                        // Program Change - 0xC0
                        case 192:
                            if (!DisableProgramChangeEvent)
                                _synth.ProgramChange(midiEvent.Channel, midiEvent.Program);
                            else
                                return;
                            break;

                        // Channel Pressure - 0xD0
                        case 208:
                            _synth.ChannelPressure(midiEvent.Channel, midiEvent.Value);
                            break;

                        // Pitch Bend - 0xE0
                        case 224:
                            _synth.PitchBend(midiEvent.Channel, midiEvent.Pitch);
                            break;

                        // Sometimes MIDI files spam these for no good reason and I can't find any info on what they are.
                        case 1:
                        case 5:
                        // MetaEvent -- SetTempo - 0x51
                        case 81:
                            // Already handled by the player.
                            return;
                        // System Messages - 0xF0
                        case 240:
                            switch ((byte)midiEvent.Control)
                            {
                                case 11:
                                    _synth.AllNotesOff(midiEvent.Channel);
                                    break;
                            }

                            break;

                        default:
                            _midiSawmill.Warning("Unhandled midi event of type {0}", midiEvent.Type, midiEvent);
                            return;
                    }
            }
            catch (FluidSynthInteropException)
            {
                // This spams NoteOff errors most of the time for no good reason.
                //_midiSawmill.Error("Exception while sending midi event of type {0}: {1}", midiEvent.Type, e, midiEvent);
            }

            _taskManager.RunOnMainThread(() => OnMidiEvent?.Invoke(midiEvent));
        }

        public void ScheduleMidiEvent(Shared.Audio.Midi.MidiEvent midiEvent, uint time, bool absolute = false)
        {
            if (Disposed) return;

            var seqEv = (SequencerEvent) midiEvent;
            seqEv.Dest = _debugEvents ? _debugRegister : _synthRegister;
            _sequencer.SendAt(seqEv, time, absolute);
        }

        public void Dispose()
        {
            Disposed = true;

            switch (Status)
            {
                case MidiRendererStatus.Input:
                    CloseInput();
                    break;
                case MidiRendererStatus.File:
                    CloseMidi();
                    break;
            }
        }

        /// <inheritdoc />
        void IMidiRenderer.InternalDispose()
        {
            Source?.Dispose();
            _driver?.Dispose();

            // Do NOT dispose of the sequencer after the synth or it'll cause a segfault for some fucking reason.
            _sequencer?.UnregisterClient(_debugRegister);
            _sequencer?.UnregisterClient(_synthRegister);
            _sequencer?.Dispose();

            _synth?.Dispose();
            _player?.Dispose();
        }
    }
}
