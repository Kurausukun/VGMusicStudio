using System;

namespace Kermalis.VGMusicStudio.Core.NDS.SDAT
{
    internal class Channel
    {
        public readonly byte Index;

        public Track Owner;
        public InstrumentType Type;
        public EnvelopeState State;
        public bool AutoSweep;
        public byte BaseKey;
        public byte Key;
        public byte NoteVelocity;
        public sbyte StartingPan;
        public sbyte Pan;
        public int SweepCounter;
        public int SweepLength;
        public short SweepPitch;
        public int Velocity; // The SEQ Player treats 0 as the 100% amplitude value and -92544 (-723*128) as the 0% amplitude value. The starting ampltitude is 0% (-92544).
        public byte Volume; // From 0x00-0x7F (Calculated from Utils)
        public ushort BaseTimer;
        public ushort Timer;
        public int NoteDuration;

        private byte _attack;
        private int _sustain;
        private ushort _decay;
        private ushort _release;

        private int _pos;
        private short _prevLeft;
        private short _prevRight;

        // PCM8, PCM16, ADPCM
        private SWAR.SWAV _swav;
        // PCM8, PCM16
        private int _dataOffset;
        // ADPCM
        private ADPCMDecoder _adpcmDecoder;
        private short _adpcmLoopLastSample;
        private short _adpcmLoopStepIndex;
        // PSG
        private byte _psgDuty;
        private int _psgCounter;
        // Noise
        private ushort _noiseCounter;

        public Channel(byte i)
        {
            Index = i;
        }

        public void StartPCM(SWAR.SWAV swav, int noteDuration)
        {
            Type = InstrumentType.PCM;
            _dataOffset = 0;
            _swav = swav;
            if (swav.Format == SWAVFormat.ADPCM)
            {
                _adpcmDecoder = new ADPCMDecoder(swav.Samples);
            }
            BaseTimer = swav.Timer;
            Start(noteDuration);
        }
        public void StartPSG(byte duty, int noteDuration)
        {
            Type = InstrumentType.PSG;
            _psgCounter = 0;
            _psgDuty = duty;
            BaseTimer = 8006;
            Start(noteDuration);
        }
        public void StartNoise(int noteLength)
        {
            Type = InstrumentType.Noise;
            _noiseCounter = 0x7FFF;
            BaseTimer = 8006;
            Start(noteLength);
        }

        private void Start(int noteDuration)
        {
            State = EnvelopeState.Attack;
            Velocity = -92544;
            _pos = 0;
            _prevLeft = _prevRight = 0;
            NoteDuration = noteDuration;
        }

        public void Stop()
        {
            if (Owner != null)
            {
                Owner.Channels.Remove(this);
            }
            Owner = null;
            Volume = 0;
        }

        public int SweepMain()
        {
            if (SweepPitch != 0 && SweepCounter < SweepLength)
            {
                int sweep = (int)(Math.BigMul(SweepPitch, SweepLength - SweepCounter) / SweepLength);
                if (AutoSweep)
                {
                    SweepCounter++;
                }
                return sweep;
            }
            else
            {
                return 0;
            }
        }

        public void SetAttack(int a)
        {
            _attack = Utils.AttackTable[a];
        }
        public void SetDecay(int d)
        {
            _decay = Utils.DecayTable[d];
        }
        public void SetSustain(byte s)
        {
            _sustain = Utils.SustainTable[s];
        }
        public void SetRelease(int r)
        {
            _release = Utils.DecayTable[r];
        }
        public void StepEnvelope()
        {
            switch (State)
            {
                case EnvelopeState.Attack:
                {
                    Velocity = _attack * Velocity / 0xFF;
                    if (Velocity == 0)
                    {
                        State = EnvelopeState.Decay;
                    }
                    break;
                }
                case EnvelopeState.Decay:
                {
                    Velocity -= _decay;
                    if (Velocity <= _sustain)
                    {
                        State = EnvelopeState.Sustain;
                        Velocity = _sustain;
                    }
                    break;
                }
                case EnvelopeState.Release:
                {
                    Velocity -= _release;
                    if (Velocity < -92544)
                    {
                        Velocity = -92544;
                    }
                    break;
                }
            }
        }
		
        public void UpdateLFO()
        {
            if (Owner.LFODelayCount > 0)
            {
                Owner.LFODelayCount--;
                Owner.LFOPhase = 0;
            }
            else
            {
                int speed = Owner.LFOSpeed << 6; // "<< 6" is "* 0x40"
                int counter = (Owner.LFOPhase + speed);
                while (counter >= 0x8000)
                {
                    counter -= 0x8000;
                }
                Owner.LFOPhase = (ushort)counter;
            }
        }

        /// <summary>EmulateProcess doesn't care about samples that loop; it only cares about ones that force the track to wait for them to end</summary>
        public void EmulateProcess()
        {
            if (Timer != 0)
            {
                int numSamples = (_pos + 0x100) / Timer;
                _pos = (_pos + 0x100) % Timer;
                for (int i = 0; i < numSamples; i++)
                {
                    if (Type == InstrumentType.PCM && !_swav.DoesLoop)
                    {
                        switch (_swav.Format)
                        {
                            case SWAVFormat.PCM8:
                            {
                                if (_dataOffset >= _swav.Samples.Length)
                                {
                                    Stop();
                                }
                                else
                                {
                                    _dataOffset++;
                                }
                                return;
                            }
                            case SWAVFormat.PCM16:
                            {
                                if (_dataOffset >= _swav.Samples.Length)
                                {
                                    Stop();
                                }
                                else
                                {
                                    _dataOffset += 2;
                                }
                                return;
                            }
                            case SWAVFormat.ADPCM:
                            {
                                if (_adpcmDecoder.DataOffset >= _swav.Samples.Length && !_adpcmDecoder.OnSecondNibble)
                                {
                                    Stop();
                                }
                                else
                                {
                                    // This is a faster emulation of adpcmDecoder.GetSample() without caring about the sample
                                    if (_adpcmDecoder.OnSecondNibble)
                                    {
                                        _adpcmDecoder.DataOffset++;
                                    }
                                    _adpcmDecoder.OnSecondNibble = !_adpcmDecoder.OnSecondNibble;
                                }
                                return;
                            }
                        }
                    }
                }
            }
        }
        public void Process(out short left, out short right)
        {
            if (Timer != 0)
            {
                int numSamples = (_pos + 0x100) / Timer;
                _pos = (_pos + 0x100) % Timer;
                // prevLeft and prevRight are stored because numSamples can be 0.
                for (int i = 0; i < numSamples; i++)
                {
                    short samp;
                    switch (Type)
                    {
                        case InstrumentType.PCM:
                        {
                            switch (_swav.Format)
                            {
                                case SWAVFormat.PCM8:
                                {
                                    // If hit end
                                    if (_dataOffset >= _swav.Samples.Length)
                                    {
                                        if (_swav.DoesLoop)
                                        {
                                            _dataOffset = _swav.LoopOffset * 4;
                                        }
                                        else
                                        {
                                            left = right = _prevLeft = _prevRight = 0;
                                            Stop();
                                            return;
                                        }
                                    }
                                    samp = (short)((sbyte)_swav.Samples[_dataOffset++] << 8);
                                    break;
                                }
                                case SWAVFormat.PCM16:
                                {
                                    // If hit end
                                    if (_dataOffset >= _swav.Samples.Length)
                                    {
                                        if (_swav.DoesLoop)
                                        {
                                            _dataOffset = _swav.LoopOffset * 4;
                                        }
                                        else
                                        {
                                            left = right = _prevLeft = _prevRight = 0;
                                            Stop();
                                            return;
                                        }
                                    }
                                    samp = (short)(_swav.Samples[_dataOffset++] | (_swav.Samples[_dataOffset++] << 8));
                                    break;
                                }
                                case SWAVFormat.ADPCM:
                                {
                                    // If just looped
                                    if (_swav.DoesLoop && _adpcmDecoder.DataOffset == _swav.LoopOffset * 4 && !_adpcmDecoder.OnSecondNibble)
                                    {
                                        _adpcmLoopLastSample = _adpcmDecoder.LastSample;
                                        _adpcmLoopStepIndex = _adpcmDecoder.StepIndex;
                                    }
                                    // If hit end
                                    if (_adpcmDecoder.DataOffset >= _swav.Samples.Length && !_adpcmDecoder.OnSecondNibble)
                                    {
                                        if (_swav.DoesLoop)
                                        {
                                            _adpcmDecoder.DataOffset = _swav.LoopOffset * 4;
                                            _adpcmDecoder.StepIndex = _adpcmLoopStepIndex;
                                            _adpcmDecoder.LastSample = _adpcmLoopLastSample;
                                            _adpcmDecoder.OnSecondNibble = false;
                                        }
                                        else
                                        {
                                            left = right = _prevLeft = _prevRight = 0;
                                            Stop();
                                            return;
                                        }
                                    }
                                    samp = _adpcmDecoder.GetSample();
                                    break;
                                }
                                default: samp = 0; break;
                            }
                            break;
                        }
                        case InstrumentType.PSG:
                        {
                            samp = _psgCounter <= _psgDuty ? short.MinValue : short.MaxValue;
                            _psgCounter++;
                            if (_psgCounter >= 8)
                            {
                                _psgCounter = 0;
                            }
                            break;
                        }
                        case InstrumentType.Noise:
                        {
                            if ((_noiseCounter & 1) != 0)
                            {
                                _noiseCounter = (ushort)((_noiseCounter >> 1) ^ 0x6000);
                                samp = -0x7FFF;
                            }
                            else
                            {
                                _noiseCounter = (ushort)(_noiseCounter >> 1);
                                samp = 0x7FFF;
                            }
                            break;
                        }
                        default: samp = 0; break;
                    }
                    samp = (short)(samp * Volume / 0x7F);
                    _prevLeft = (short)(samp * (-Pan + 0x40) / 0x80);
                    _prevRight = (short)(samp * (Pan + 0x40) / 0x80);
                }
            }
            left = _prevLeft;
            right = _prevRight;
        }
    }
}
