using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TL;

namespace TGFileDownloader
{
    internal static class Extension
    {
        public static string? GetMIME(this Storage_FileType type)
        {
            return type switch
            {
                Storage_FileType.jpeg => "image/jpeg",
                Storage_FileType.gif => "image/gif",
                Storage_FileType.png => "image/png",
                Storage_FileType.pdf => "application/pdf",
                Storage_FileType.mp3 => "audio/mpeg",
                Storage_FileType.mov => "videp/quicktime",
                Storage_FileType.mp4 => "videp/mp4",
                Storage_FileType.webp => "image/webp",
                _ => null,
            };
        }
    }
}
