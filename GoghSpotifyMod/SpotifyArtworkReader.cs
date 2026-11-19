using System;
using System.IO;
using Windows.Storage.Streams;

public static class SpotifyArtworkReader
{
    public static byte[] Read(object value)
    {
        using (var stream = ((IRandomAccessStream)value).AsStreamForRead())
        using (var buffer = new MemoryStream())
        {
            if (stream.Length > 1048576)
                return new byte[0];

            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }
}

