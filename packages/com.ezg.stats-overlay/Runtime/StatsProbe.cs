using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace EZG.StatsOverlay
{
    /// <summary>
    /// Nguồn số liệu cho overlay: tự đo FPS mỗi frame + đọc counter của Unity qua <see cref="ProfilerRecorder"/>.
    ///
    /// GIỚI HẠN CÓ THẬT (không phải bug): counter render/memory chỉ được Unity bơm số trong
    /// <b>Editor</b> và <b>Development Build</b>. Build Release strip profiler ⇒ recorder trả
    /// <c>Valid == false</c> ⇒ getter trả <c>null</c> ⇒ view in "n/a". Riêng FPS/frame time/độ phân giải
    /// luôn đúng ở mọi loại build vì tự đo, không qua profiler.
    ///
    /// Tên counter lấy đúng theo bảng của Unity 6 (ProfilerRecorderHandle.GetAvailable). Đổi bản Unity mà
    /// counter bị đổi tên thì recorder thành invalid → hiện "n/a", KHÔNG ném exception.
    /// </summary>
    internal sealed class StatsProbe : IDisposable
    {
        /// <summary>Số mẫu giữ lại cho counter thời gian để lấy trung bình (giảm nhảy số).</summary>
        private const int TimeSampleCapacity = 15;

        private readonly List<ProfilerRecorder> _all = new List<ProfilerRecorder>(32);

        // --- Thời gian (ns) ---
        private ProfilerRecorder _cpuMain;
        private ProfilerRecorder _cpuRender;
        private ProfilerRecorder _gpu;

        // --- Render ---
        private ProfilerRecorder _batches;
        private ProfilerRecorder _setPassCalls;
        private ProfilerRecorder _drawCalls;
        private ProfilerRecorder _triangles;
        private ProfilerRecorder _vertices;
        private ProfilerRecorder _shadowCasters;
        private ProfilerRecorder _skinnedMeshes;

        // --- Thành phần để tính "saved by batching" (đúng công thức cửa sổ Statistics của Editor) ---
        private ProfilerRecorder _staticBatches;
        private ProfilerRecorder _staticBatchedDrawCalls;
        private ProfilerRecorder _dynamicBatches;
        private ProfilerRecorder _dynamicBatchedDrawCalls;
        private ProfilerRecorder _instancedBatches;
        private ProfilerRecorder _instancedBatchedDrawCalls;

        // --- Memory (bytes) ---
        private ProfilerRecorder _systemUsedMemory;
        private ProfilerRecorder _totalUsedMemory;
        private ProfilerRecorder _totalReservedMemory;
        private ProfilerRecorder _gcUsedMemory;
        private ProfilerRecorder _gcReservedMemory;
        private ProfilerRecorder _gcAllocInFrame;
        private ProfilerRecorder _textureMemory;
        private ProfilerRecorder _textureCount;
        private ProfilerRecorder _meshMemory;

        private bool _running;

        // --- Cửa sổ đo FPS ---
        private float _windowTime;
        private int _windowFrames;
        private float _windowWorstDelta;
        private float _windowBestDelta = float.MaxValue;

        public float Fps { get; private set; }
        public float FrameMs { get; private set; }
        public float WorstFrameMs { get; private set; }
        public float BestFrameMs { get; private set; }

        /// <summary>True khi ít nhất 1 counter render đọc được (Editor / Development Build).</summary>
        public bool HasProfilerCounters => _batches.Valid || _setPassCalls.Valid || _triangles.Valid;

        public void Start()
        {
            if (_running) return;
            _running = true;

            _cpuMain = New(ProfilerCategory.Render, "CPU Main Thread Frame Time", TimeSampleCapacity);
            _cpuRender = New(ProfilerCategory.Render, "CPU Render Thread Frame Time", TimeSampleCapacity);
            _gpu = New(ProfilerCategory.Render, "GPU Frame Time", TimeSampleCapacity);

            _batches = New(ProfilerCategory.Render, "Batches Count");
            _setPassCalls = New(ProfilerCategory.Render, "SetPass Calls Count");
            _drawCalls = New(ProfilerCategory.Render, "Draw Calls Count");
            _triangles = New(ProfilerCategory.Render, "Triangles Count");
            _vertices = New(ProfilerCategory.Render, "Vertices Count");
            _shadowCasters = New(ProfilerCategory.Render, "Shadow Casters Count");
            _skinnedMeshes = New(ProfilerCategory.Render, "Visible Skinned Meshes Count");

            _staticBatches = New(ProfilerCategory.Render, "Static Batches Count");
            _staticBatchedDrawCalls = New(ProfilerCategory.Render, "Static Batched Draw Calls Count");
            _dynamicBatches = New(ProfilerCategory.Render, "Dynamic Batches Count");
            _dynamicBatchedDrawCalls = New(ProfilerCategory.Render, "Dynamic Batched Draw Calls Count");
            _instancedBatches = New(ProfilerCategory.Render, "Instanced Batches Count");
            _instancedBatchedDrawCalls = New(ProfilerCategory.Render, "Instanced Batched Draw Calls Count");

            _systemUsedMemory = New(ProfilerCategory.Memory, "System Used Memory");
            _totalUsedMemory = New(ProfilerCategory.Memory, "Total Used Memory");
            _totalReservedMemory = New(ProfilerCategory.Memory, "Total Reserved Memory");
            _gcUsedMemory = New(ProfilerCategory.Memory, "GC Used Memory");
            _gcReservedMemory = New(ProfilerCategory.Memory, "GC Reserved Memory");
            _gcAllocInFrame = New(ProfilerCategory.Memory, "GC Allocated In Frame");
            _textureMemory = New(ProfilerCategory.Memory, "Texture Memory");
            _textureCount = New(ProfilerCategory.Memory, "Texture Count");
            _meshMemory = New(ProfilerCategory.Memory, "Mesh Memory");

            ResetWindow();
        }

        /// <summary>Dừng + giải phóng toàn bộ recorder (gọi khi overlay ẩn để không tốn gì khi tắt cheat).</summary>
        public void Stop()
        {
            if (!_running) return;
            _running = false;

            for (int i = 0; i < _all.Count; i++)
            {
                ProfilerRecorder r = _all[i];
                if (r.Valid) r.Dispose();
            }
            _all.Clear();

            _cpuMain = default;
            _cpuRender = default;
            _gpu = default;
            _batches = default;
            _setPassCalls = default;
            _drawCalls = default;
            _triangles = default;
            _vertices = default;
            _shadowCasters = default;
            _skinnedMeshes = default;
            _staticBatches = default;
            _staticBatchedDrawCalls = default;
            _dynamicBatches = default;
            _dynamicBatchedDrawCalls = default;
            _instancedBatches = default;
            _instancedBatchedDrawCalls = default;
            _systemUsedMemory = default;
            _totalUsedMemory = default;
            _totalReservedMemory = default;
            _gcUsedMemory = default;
            _gcReservedMemory = default;
            _gcAllocInFrame = default;
            _textureMemory = default;
            _textureCount = default;
            _meshMemory = default;
        }

        public void Dispose() => Stop();

        /// <summary>Gọi MỖI frame — gom delta time cho cửa sổ FPS hiện tại.</summary>
        public void Tick(float unscaledDeltaTime)
        {
            if (unscaledDeltaTime <= 0f) return;

            _windowTime += unscaledDeltaTime;
            _windowFrames++;
            if (unscaledDeltaTime > _windowWorstDelta) _windowWorstDelta = unscaledDeltaTime;
            if (unscaledDeltaTime < _windowBestDelta) _windowBestDelta = unscaledDeltaTime;
        }

        /// <summary>Chốt cửa sổ FPS hiện tại thành số hiển thị rồi mở cửa sổ mới.</summary>
        public void CommitWindow()
        {
            if (_windowFrames > 0 && _windowTime > 0f)
            {
                Fps = _windowFrames / _windowTime;
                FrameMs = _windowTime / _windowFrames * 1000f;
                WorstFrameMs = _windowWorstDelta * 1000f;
                BestFrameMs = _windowBestDelta * 1000f;
            }

            ResetWindow();
        }

        private void ResetWindow()
        {
            _windowTime = 0f;
            _windowFrames = 0;
            _windowWorstDelta = 0f;
            _windowBestDelta = float.MaxValue;
        }

        // --- Getter: null = counter không có ở build này (Release strip profiler) ---

        public double? CpuMainMs => AverageMs(_cpuMain);
        public double? CpuRenderMs => AverageMs(_cpuRender);
        public double? GpuMs => AverageMs(_gpu);

        public long? Batches => Last(_batches);
        public long? SetPassCalls => Last(_setPassCalls);
        public long? DrawCalls => Last(_drawCalls);
        public long? Triangles => Last(_triangles);
        public long? Vertices => Last(_vertices);
        public long? ShadowCasters => Last(_shadowCasters);
        public long? SkinnedMeshes => Last(_skinnedMeshes);

        public long? SystemUsedMemory => Last(_systemUsedMemory);
        public long? TotalUsedMemory => Last(_totalUsedMemory);
        public long? TotalReservedMemory => Last(_totalReservedMemory);
        public long? GcUsedMemory => Last(_gcUsedMemory);
        public long? GcReservedMemory => Last(_gcReservedMemory);
        public long? GcAllocInFrame => Last(_gcAllocInFrame);
        public long? TextureMemory => Last(_textureMemory);
        public long? TextureCount => Last(_textureCount);
        public long? MeshMemory => Last(_meshMemory);

        /// <summary>
        /// "Saved by batching" — số draw call tiết kiệm được nhờ gộp batch, đúng công thức cửa sổ
        /// Statistics của Editor: tổng (draw call đã gộp − batch tương ứng) của static + dynamic + instancing.
        /// </summary>
        public long? SavedByBatching
        {
            get
            {
                if (!_staticBatchedDrawCalls.Valid || !_dynamicBatchedDrawCalls.Valid || !_instancedBatchedDrawCalls.Valid)
                    return null;

                long saved = _staticBatchedDrawCalls.LastValue - _staticBatches.LastValue
                             + _dynamicBatchedDrawCalls.LastValue - _dynamicBatches.LastValue
                             + _instancedBatchedDrawCalls.LastValue - _instancedBatches.LastValue;
                return saved < 0 ? 0 : saved;
            }
        }

        /// <summary>
        /// Ước lượng bộ nhớ backbuffer y như dòng "Screen: WxH - N MB" của Editor:
        /// mỗi pixel tính 12 byte (color front + back + depth/stencil, 4 byte mỗi cái).
        /// </summary>
        public static long ScreenBytes => (long)Screen.width * Screen.height * 12L;

        private ProfilerRecorder New(ProfilerCategory category, string statName, int capacity = 1)
        {
            // Counter không tồn tại ở build/phiên bản này → recorder invalid, KHÔNG throw.
            ProfilerRecorder recorder = ProfilerRecorder.StartNew(category, statName, capacity);
            if (recorder.Valid) _all.Add(recorder);
            return recorder;
        }

        private static long? Last(ProfilerRecorder recorder)
        {
            return recorder.Valid ? recorder.LastValue : (long?)null;
        }

        /// <summary>Trung bình các mẫu của counter thời gian (ns) → ms. Null nếu counter không có.</summary>
        private static double? AverageMs(ProfilerRecorder recorder)
        {
            if (!recorder.Valid) return null;

            int count = recorder.Count;
            if (count <= 0) return 0d;

            double sum = 0d;
            for (int i = 0; i < count; i++) sum += recorder.GetSample(i).Value;
            return sum / count * 1e-6d;
        }
    }
}
