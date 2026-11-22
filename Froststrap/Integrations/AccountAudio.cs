using NAudio.Vorbis;
using NAudio.Wave;

namespace Froststrap.Integrations;

internal static class AccountAudio
{
    private static WaveOutEvent? _output;
    private static WaveStream? _reader;
    private static float _volume = 0.8f;

    public static bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    public static float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_output is not null)
                _output.Volume = _volume;
        }
    }

    public static void Play(byte[] bytes)
    {
        Stop();
        var audio = AccountStudioClient.ExtractAudio(bytes, out var extension);
        var stream = new MemoryStream(audio);
        WaveStream reader = extension switch
        {
            "ogg" => new VorbisWaveReader(stream),
            "mp3" => new Mp3FileReader(stream),
            "wav" => new WaveFileReader(stream),
            _ => throw new RobloxApiException("This audio format cannot be played here. Download still saves the file.")
        };

        var output = new WaveOutEvent { Volume = _volume };
        output.Init(reader);
        output.Play();
        _output = output;
        _reader = reader;
    }

    public static void Stop()
    {
        var output = _output;
        var reader = _reader;
        _output = null;
        _reader = null;
        if (output is not null)
        {
            output.Stop();
            output.Dispose();
        }

        reader?.Dispose();
    }
}
