// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Graphics.OpenGLCore.Buffers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Rendering.Vertices;
using osu.Framework.Statistics;

namespace osu.Framework.Graphics.OpenGLCore.Batches
{
    internal abstract class GLCoreVertexBatch<T> : IVertexBatch<T>
        where T : unmanaged, IEquatable<T>, IVertex
    {
        public List<GLCoreVertexBuffer<T>> VertexBuffers = new List<GLCoreVertexBuffer<T>>();

        /// <summary>
        /// The number of vertices in each VertexBuffer.
        /// </summary>
        public int Size { get; }

        private int changeBeginIndex = -1;
        private int changeEndIndex = -1;

        private int currentBufferIndex;
        private int currentVertexIndex;

        private readonly GLCoreRenderer renderer;
        private readonly int maxBuffers;

        private GLCoreVertexBuffer<T> currentVertexBuffer => VertexBuffers[currentBufferIndex];

        protected GLCoreVertexBatch(GLCoreRenderer renderer, int bufferSize, int maxBuffers)
        {
            Size = bufferSize;
            this.renderer = renderer;
            this.maxBuffers = maxBuffers;

            AddAction = Add;
        }

        #region Disposal

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (GLCoreVertexBuffer<T> vbo in VertexBuffers)
                    vbo.Dispose();
            }
        }

        #endregion

        void IVertexBatch.ResetCounters()
        {
            changeBeginIndex = -1;
            currentBufferIndex = 0;
            currentVertexIndex = 0;
        }

        protected abstract GLCoreVertexBuffer<T> CreateVertexBuffer(GLCoreRenderer renderer);

        /// <summary>
        /// Adds a vertex to this <see cref="GLCoreVertexBatch{T}"/>.
        /// </summary>
        /// <param name="v">The vertex to add.</param>
        public void Add(T v)
        {
            renderer.SetActiveBatch(this);

            if (currentBufferIndex < VertexBuffers.Count && currentVertexIndex >= currentVertexBuffer.Size)
            {
                Draw();
                FrameStatistics.Increment(StatisticsCounterType.VBufOverflow);
            }

            // currentIndex will change after Draw() above, so this cannot be in an else-condition
            while (currentBufferIndex >= VertexBuffers.Count)
                VertexBuffers.Add(CreateVertexBuffer(renderer));

            if (currentVertexBuffer.SetVertex(currentVertexIndex, v))
            {
                if (changeBeginIndex == -1)
                    changeBeginIndex = currentVertexIndex;

                changeEndIndex = currentVertexIndex + 1;
            }

            ++currentVertexIndex;
        }

        /// <summary>
        /// Adds a vertex to this <see cref="GLCoreVertexBatch{T}"/>.
        /// This is a cached delegate of <see cref="Add"/> that should be used in memory-critical locations such as <see cref="DrawNode"/>s.
        /// </summary>
        public Action<T> AddAction { get; private set; }

        public int Draw()
        {
            if (currentVertexIndex == 0)
                return 0;

            GLCoreVertexBuffer<T> vertexBuffer = currentVertexBuffer;
            if (changeBeginIndex >= 0)
                vertexBuffer.UpdateRange(changeBeginIndex, changeEndIndex);

            vertexBuffer.DrawRange(0, currentVertexIndex);

            int count = currentVertexIndex;

            // When using multiple buffers we advance to the next one with every draw to prevent contention on the same buffer with future vertex updates.
            //TODO: let us know if we exceed and roll over to zero here.
            currentBufferIndex = (currentBufferIndex + 1) % maxBuffers;
            currentVertexIndex = 0;
            changeBeginIndex = -1;

            FrameStatistics.Increment(StatisticsCounterType.DrawCalls);
            FrameStatistics.Add(StatisticsCounterType.VerticesDraw, count);

            return count;
        }
    }
}
