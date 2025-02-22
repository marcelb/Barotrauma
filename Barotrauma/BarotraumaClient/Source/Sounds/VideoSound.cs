﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenAL;
using Microsoft.Xna.Framework;
using System.Runtime.InteropServices;
using System.Threading;
using Barotrauma.Media;

namespace Barotrauma.Sounds
{
    public class VideoSound : Sound
    {
        private readonly object mutex;
        private Queue<short[]> sampleQueue;

        private SoundChannel soundChannel;
        private Video video;

        public VideoSound(SoundManager owner, string filename, int sampleRate, Video vid) : base(owner, filename, true, false)
        {
            ALFormat = Al.FormatStereo16;
            SampleRate = sampleRate;

            sampleQueue = new Queue<short[]>();
            mutex = new object();

            soundChannel = null;

            video = vid;
        }

        public override bool IsPlaying()
        {
            bool retVal = false;
            lock (mutex)
            {
                retVal = soundChannel != null && soundChannel.IsPlaying;
            }
            return retVal;
        }

        public void Enqueue(short[] buf)
        {
            lock (mutex)
            {
                sampleQueue.Enqueue(buf);
            }
        }

        public override SoundChannel Play(float gain, float range, Vector2 position, bool muffle = false)
        {
            throw new InvalidOperationException();
        }

        public override SoundChannel Play(Vector3? position, float gain, bool muffle = false)
        {
            throw new InvalidOperationException();
        }

        public override SoundChannel Play(float gain)
        {
            SoundChannel chn = null;
            lock (mutex)
            {
                if (soundChannel != null) soundChannel.Dispose();
                chn = new SoundChannel(this, gain, null, 1.0f, 3.0f, "video", false);
                soundChannel = chn;
            }
            return chn;
        }

        public override SoundChannel Play()
        {
            return Play(0.5f);
        }

        public override int FillStreamBuffer(int samplePos, short[] buffer)
        {
            if (!video.IsPlaying) return -1;

            short[] buf;
            int readAmount = 0;
            lock (mutex)
            {
                while (readAmount<buffer.Length)
                {
                    if (sampleQueue.Count == 0) break;
                    buf = sampleQueue.Peek();
                    if (readAmount + buf.Length >= buffer.Length) break;
                    buf = sampleQueue.Dequeue();
                    buf.CopyTo(buffer, readAmount);
                    readAmount += buf.Length;
                }
            }
            return readAmount*2;
        }

        public override void Dispose()
        {
            lock (mutex)
            {
                if (soundChannel != null)
                {
                    soundChannel.Dispose();
                }
                base.Dispose();
            }
        }
    }
}
