using NAudio.Dsp;
using NAudio.Wave;

namespace MINWallPaper.Widgets;

/// <summary>
/// 系统声音频谱。WASAPI 回环只开一路，所有音浪组件共享同一份 64 段频谱，
/// 这样加多个音浪不会重复采集，也不会互相抢设备。
/// </summary>
internal sealed class AudioSpectrum
{
    public const int Bands = 64;

    private const int FftSize = 1024;
    private const int FftPower = 10;

    private static readonly Lazy<AudioSpectrum> Lazy = new(() => new AudioSpectrum());

    public static AudioSpectrum Instance => Lazy.Value;

    private readonly float[] _buffer = new float[FftSize];
    private readonly Complex[] _fft = new Complex[FftSize];
    private readonly float[] _levels = new float[Bands];
    private readonly float[] _peaks = new float[Bands];
    private readonly object _sync = new();

    private WasapiLoopbackCapture? _capture;
    private bool _usingFloat = true;
    private int _buffered;
    private int _refs;
    private float _energy;

    /// <summary>每个音浪组件启动时引用一次，最后一个关闭时停掉采集。</summary>
    public void Acquire()
    {
        lock (_sync)
        {
            _refs++;
            if (_refs == 1)
            {
                StartCapture();
            }
        }
    }

    public void Release()
    {
        lock (_sync)
        {
            _refs = Math.Max(0, _refs - 1);
            if (_refs == 0)
            {
                StopCapture();
            }
        }
    }

    /// <summary>把当前 64 段频谱复制到目标数组。</summary>
    public void CopyTo(float[] destination)
    {
        lock (_sync)
        {
            int count = Math.Min(destination.Length, Bands);
            Array.Copy(_levels, destination, count);
        }
    }

    private void StartCapture()
    {
        try
        {
            _capture = new WasapiLoopbackCapture();
            _usingFloat = _capture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat
                          && _capture.WaveFormat.BitsPerSample == 32;
            _capture.DataAvailable += OnDataAvailable;
            _capture.StartRecording();
            Log.Write($"音浪采集已启动 {_capture.WaveFormat}");
        }
        catch (Exception ex)
        {
            Log.Write("音浪采集启动失败: " + ex.Message);
            _capture = null;
        }
    }

    private void StopCapture()
    {
        if (_capture == null)
        {
            return;
        }

        try
        {
            _capture.StopRecording();
        }
        catch
        {
            // 退出阶段忽略
        }

        _capture.DataAvailable -= OnDataAvailable;
        _capture.Dispose();
        _capture = null;
        Log.Write("音浪采集已停止");
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_capture == null || e.BytesRecorded == 0)
        {
            return;
        }

        int channels = _capture.WaveFormat.Channels;
        int bytesPerSample = _capture.WaveFormat.BitsPerSample / 8;
        int frameSize = bytesPerSample * channels;

        if (frameSize <= 0)
        {
            return;
        }

        int frames = e.BytesRecorded / frameSize;

        lock (_sync)
        {
            for (int i = 0; i < frames; i++)
            {
                int offset = i * frameSize;
                float value;

                if (_usingFloat)
                {
                    value = BitConverter.ToSingle(e.Buffer, offset);
                }
                else
                {
                    short raw = BitConverter.ToInt16(e.Buffer, offset);
                    value = raw / 32768f;
                }

                _buffer[_buffered++] = value;

                if (_buffered >= FftSize)
                {
                    Analyze();
                    _buffered = 0;
                }
            }
        }
    }

    /// <summary>在采集回调里做一次 FFT，写进 64 段频谱。</summary>
    private void Analyze()
    {
        // 时域整体能量：让小声时整条频谱也会一起动，而不是只有低频几根在跳。
        float sumSquares = 0;

        for (int i = 0; i < FftSize; i++)
        {
            sumSquares += _buffer[i] * _buffer[i];
        }

        float rms = (float)Math.Sqrt(sumSquares / FftSize);
        float instant = Math.Clamp(rms * 3.4f, 0f, 1f);
        _energy = instant > _energy ? instant : _energy * 0.9f;

        for (int i = 0; i < FftSize; i++)
        {
            float window = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * i / (FftSize - 1))));
            _fft[i].X = _buffer[i] * window;
            _fft[i].Y = 0;
        }

        FastFourierTransform.FFT(true, FftPower, _fft);

        for (int band = 0; band < Bands; band++)
        {
            int start = (int)Math.Pow(2, band * 9.0 / Bands);
            int end = Math.Max(start + 1, (int)Math.Pow(2, (band + 1) * 9.0 / Bands));

            float sum = 0;
            int count = 0;

            for (int j = start; j < end && j < FftSize / 2; j++)
            {
                sum += (float)Math.Sqrt(_fft[j].X * _fft[j].X + _fft[j].Y * _fft[j].Y);
                count++;
            }

            float raw = count > 0 ? sum / count : 0;

            // 静音时不再更新峰值，避免自动增益把底噪放成满屏。
            if (raw < 0.00004f && _energy < 0.02f)
            {
                _levels[band] *= 0.86f;
                continue;
            }

            float value = (float)(Math.Log10(1 + raw * 14) * 1.4);

            // 每个频段自己的峰值，低频不会把高频压没。
            if (_peaks[band] < 0.05f)
            {
                _peaks[band] = 0.5f;
            }

            _peaks[band] = Math.Max(value, _peaks[band] * 0.9995f);

            float normalized = Math.Clamp(value / Math.Max(_peaks[band], 0.05f), 0f, 1f);

            // 高频稍微抬一点，整体再和响度混合，形成整条波都在震的效果。
            float tilt = 0.9f + band / (float)Bands * 0.5f;
            normalized = (float)Math.Pow(Math.Clamp(normalized * tilt, 0f, 1f), 0.72);
            normalized = Math.Max(normalized, _energy * 0.45f);

            _levels[band] = normalized > _levels[band]
                ? normalized
                : _levels[band] * 0.84f;
        }
    }
}
