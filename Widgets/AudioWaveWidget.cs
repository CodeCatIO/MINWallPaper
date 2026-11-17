using System.Drawing;
using System.Drawing.Drawing2D;
using NAudio.Dsp;
using NAudio.Wave;

namespace MINWallPaper.Widgets;

/// <summary>
/// 音浪组件：用 WASAPI 回环采集系统正在播放的声音，做 FFT 后画成频谱柱。
/// 不需要 hook 任何播放器，静音时柱子会自然落下。
/// </summary>
internal sealed class AudioWaveWidget : WidgetWindow
{
    private const int BarCount = 48;
    private const int FftSize = 1024;
    private const int FftPower = 10; // 2^10 = 1024

    private readonly float[] _levels = new float[BarCount];
    private readonly float[] _buffer = new float[FftSize];
    private readonly Complex[] _fft = new Complex[FftSize];
    private readonly object _sync = new();

    private WasapiLoopbackCapture? _capture;
    private int _buffered;
    private bool _usingFloat = true;

    public AudioWaveWidget() : base(560, 128, 33)
    {
        StartCapture();
    }

    protected override void PlaceWindow()
    {
        PlaceBottomCenter();
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
        }
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
            for (int i = 0; i < frames && _buffered < FftSize; i++)
            {
                int offset = i * frameSize;
                float value;

                if (_usingFloat)
                {
                    value = BitConverter.ToSingle(e.Buffer, offset);
                }
                else
                {
                    // 16 位整数格式转成 -1~1
                    short raw = BitConverter.ToInt16(e.Buffer, offset);
                    value = raw / 32768f;
                }

                _buffer[_buffered++] = value;
            }
        }
    }

    protected override void Paint(Graphics g, Size size)
    {
        var rect = new Rectangle(0, 0, size.Width - 1, size.Height - 1);
        using GraphicsPath path = Round.Create(rect, 22);

        using (var fill = new SolidBrush(Color.FromArgb(104, 16, 18, 24)))
        {
            g.FillPath(fill, path);
        }

        using (var edge = new Pen(Color.FromArgb(40, 255, 255, 255), 1.2f))
        {
            g.DrawPath(edge, path);
        }

        UpdateSpectrum();

        int left = 18;
        int right = size.Width - 18;
        int bottom = size.Height - 18;
        int maxHeight = size.Height - 36;
        float slot = (right - left) / (float)BarCount;
        int barWidth = Math.Max(2, (int)(slot * 0.62f));

        for (int i = 0; i < BarCount; i++)
        {
            float level = Math.Clamp(_levels[i], 0f, 1f);
            int height = Math.Max(2, (int)(level * maxHeight));
            int x = left + (int)(i * slot);
            int y = bottom - height;

            using var brush = new LinearGradientBrush(
                new Rectangle(x, y, barWidth, height),
                Color.FromArgb(150, 226, 236, 248),
                Color.FromArgb(235, 255, 255, 255),
                LinearGradientMode.Vertical);

            g.FillRectangle(brush, x, y, barWidth, height);
        }
    }

    private void UpdateSpectrum()
    {
        lock (_sync)
        {
            if (_buffered < FftSize)
            {
                return;
            }

            _buffered = 0;
        }

        // 加汉宁窗，减少频谱泄漏。
        for (int i = 0; i < FftSize; i++)
        {
            float window = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * i / (FftSize - 1))));
            _fft[i].X = _buffer[i] * window;
            _fft[i].Y = 0;
        }

        FastFourierTransform.FFT(true, FftPower, _fft);

        for (int i = 0; i < BarCount; i++)
        {
            int start = (int)Math.Pow(2, i * 9.0 / BarCount);
            int end = Math.Max(start + 1, (int)Math.Pow(2, (i + 1) * 9.0 / BarCount));

            float sum = 0;
            int count = 0;

            for (int j = start; j < end && j < FftSize / 2; j++)
            {
                sum += (float)Math.Sqrt(_fft[j].X * _fft[j].X + _fft[j].Y * _fft[j].Y);
                count++;
            }

            float value = count > 0 ? sum / count : 0;

            // 对数压缩，让小声也有反应。
            value = (float)(Math.Log10(1 + value * 14) * 1.4);
            value = Math.Clamp(value, 0, 1);

            // 上升快、下降慢，视觉上更自然。
            _levels[i] = value > _levels[i]
                ? value
                : _levels[i] * 0.84f;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _capture != null)
        {
            try
            {
                _capture.StopRecording();
            }
            catch
            {
                // 关闭阶段忽略
            }

            _capture.DataAvailable -= OnDataAvailable;
            _capture.Dispose();
            _capture = null;
        }

        base.Dispose(disposing);
    }
}
