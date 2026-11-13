using System;
using System.IO;
using System.Text;
using TGramFS.Models;

namespace TGramFS.Services
{
    /// <summary>
    /// Парсер аудио-метаданных (ID3v1, ID3v2, FLAC, M4A) на основе легковесного чтения первых байт потока (Header Cache).
    /// Позволяет извлекать реальные названия треков, исполнителей и формат даже из временных файлов (.tmp).
    /// </summary>
    public static class AudioMetadataExtractor
    {
        public const int HeaderCacheSize = 262144; // 256 KB для надежного чтения ID3v2 тегов и обложки альбома (APIC)
        // ВРЕМЕННЫЙ ФЛАГ ДЛЯ ТЕСТИРОВАНИЯ bad_audio_tags.txt:
        // отключает автодекодирование legacy-кодировок (CP1251/UTF-8 в 0x00 и ID3v1) в NormalizeTagValue и DecodeLegacyBytes.
        private static readonly bool TemporarilyDisableLegacyEncodingDecoder = true;
        private static readonly Encoding Cp1251Encoding;
        private static readonly Encoding StrictUtf8Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        static AudioMetadataExtractor()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Cp1251Encoding = Encoding.GetEncoding(1251);
            try
            {
                // Сохраняем байты тегов без явной кодировки (ID3v1 и др.) 1-в-1 без потерь,
                // чтобы затем точно распознать UTF-8 / Windows-1251 / ISO-8859-1.
                ATL.Settings.DefaultTextEncoding = Encoding.Latin1;
            }
            catch { }
        }

        public static bool IsAudioFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext == ".mp3" || ext == ".flac" || ext == ".m4a" || ext == ".ogg" || ext == ".wav" || ext == ".aac" || ext == ".opus" || ext == ".wma";
        }

        public static bool IsPotentialAudio(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            if (IsAudioFile(fileName)) return true;
            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext == ".tmp" || ext == ".temp" || ext == ".part" || ext == ".crdownload";
        }

        public static string? DetectAudioFormat(byte[] buffer, int length)
        {
            if (buffer == null || length < 4) return null;

            // ID3v2 заголовок: 'ID3' (MP3)
            if (length >= 3 && buffer[0] == 0x49 && buffer[1] == 0x44 && buffer[2] == 0x33)
                return ".mp3";

            // MPEG Frame Sync: 11 бит единиц (0xFF, 0xE0+)
            if (length >= 2 && buffer[0] == 0xFF && (buffer[1] & 0xE0) == 0xE0)
                return ".mp3";

            // FLAC: 'fLaC'
            if (length >= 4 && buffer[0] == 0x66 && buffer[1] == 0x4C && buffer[2] == 0x61 && buffer[3] == 0x43)
                return ".flac";

            // OGG: 'OggS'
            if (length >= 4 && buffer[0] == 0x4F && buffer[1] == 0x67 && buffer[2] == 0x67 && buffer[3] == 0x53)
                return ".ogg";

            // M4A / AAC: 'ftyp' на смещении 4-7
            if (length >= 8 && buffer[4] == 0x66 && buffer[5] == 0x74 && buffer[6] == 0x79 && buffer[7] == 0x70)
                return ".m4a";

            // RIFF WAVE: 'RIFF' .... 'WAVE'
            if (length >= 12 && buffer[0] == 0x52 && buffer[1] == 0x49 && buffer[2] == 0x46 && buffer[3] == 0x46 &&
                buffer[8] == 0x57 && buffer[9] == 0x41 && buffer[10] == 0x56 && buffer[11] == 0x45)
                return ".wav";

            return null;
        }

        /// <summary>
        /// Извлекает метаданные и обложку из локального файла с помощью библиотеки ATL.
        /// </summary>
        public static AudioMetadataResult ExtractFromFile(string filePath, string? originalFileName = null)
        {
            var result = new AudioMetadataResult();
            string? rawTitle = null;
            string? rawArtist = null;
            string? rawAlbum = null;
            try
            {
                if (File.Exists(filePath))
                {
                    var track = new ATL.Track(filePath);
                    rawTitle = track.Title;
                    rawArtist = track.Artist;
                    rawAlbum = track.Album;

                    result.Title = NormalizeTagValue(rawTitle);
                    result.Artist = NormalizeTagValue(rawArtist);
                    result.Album = NormalizeTagValue(rawAlbum);
                    result.Genre = NormalizeTagValue(track.Genre);
                    result.Year = track.Year > 0 ? track.Year : null;
                    result.TrackNumber = track.TrackNumber > 0 ? track.TrackNumber : null;
                    result.DurationSeconds = track.Duration > 0 ? track.Duration : null;
                    result.Bitrate = track.Bitrate > 0 ? track.Bitrate : null;
                    string effectiveFileName = !string.IsNullOrWhiteSpace(originalFileName) ? originalFileName : filePath;
                    result.AudioFormat = Path.GetExtension(effectiveFileName)?.ToLowerInvariant();

                    // Если ATL подставил имя локального файла вместо реального тега ID3
                    string fileNoExt = Path.GetFileNameWithoutExtension(filePath);
                    if (!string.IsNullOrEmpty(result.Title) &&
                        (result.Title.Equals(fileNoExt, StringComparison.OrdinalIgnoreCase) ||
                         result.Title.Equals(Path.GetFileName(filePath), StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Title = null;
                    }

                    if (track.EmbeddedPictures != null && track.EmbeddedPictures.Count > 0)
                    {
                        var pic = track.EmbeddedPictures[0];
                        if (pic.PictureData != null && pic.PictureData.Length > 0)
                        {
                            result.AlbumCover = pic.PictureData;
                        }
                    }

                    result.HasBadTags = CheckForBadTags(filePath, effectiveFileName, rawTitle, rawArtist, rawAlbum, result);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Debug("AudioMetadataExtractor", $"ATL чтение файла '{filePath}': {ex.Message}");
            }

            if (string.IsNullOrEmpty(result.Title))
            {
                string nameForInference = !string.IsNullOrWhiteSpace(originalFileName)
                    ? originalFileName
                    : Path.GetFileName(filePath);
                InferFromFileName(nameForInference, result);
            }

            return result;
        }

        /// <summary>
        /// Извлекает кэш первых 256 КБ заголовка и парсит аудио-теги
        /// </summary>
        public static AudioMetadataResult ExtractFromStream(Stream stream, string fileName)
        {
            var result = new AudioMetadataResult();
            byte[] headerBuffer = new byte[HeaderCacheSize];
            int readBytes = 0;

            try
            {
                long originalPos = stream.CanSeek ? stream.Position : 0;
                readBytes = stream.Read(headerBuffer, 0, headerBuffer.Length);

                bool hasCorruptedId3v1Tail = false;
                byte[]? id3v1Tail = null;
                if (stream.CanSeek && stream.Length >= 128 &&
                    (fileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || AudioMetadataExtractor.DetectAudioFormat(headerBuffer, readBytes) == ".mp3"))
                {
                    byte[] tail = new byte[128];
                    stream.Seek(-128, SeekOrigin.End);
                    int tailRead = 0;
                    while (tailRead < 128)
                    {
                        int r = stream.Read(tail, tailRead, 128 - tailRead);
                        if (r <= 0) break;
                        tailRead += r;
                    }
                    if (tailRead == 128)
                    {
                        id3v1Tail = tail;
                        hasCorruptedId3v1Tail = HasCorruptedId3v1Tail(tail, 0);
                    }
                }

                if (stream.CanSeek)
                {
                    stream.Position = originalPos; // Возвращаем позицию потока для дальнейшей записи в TG
                }

                if (readBytes > 0)
                {
                    result.AudioFormat = DetectAudioFormat(headerBuffer, readBytes);

                    // Базовый парсинг ID3v2 (первые 3 байта 'ID3')
                    if (readBytes >= 10 && headerBuffer[0] == 0x49 && headerBuffer[1] == 0x44 && headerBuffer[2] == 0x33)
                    {
                        ParseId3v2Header(headerBuffer, readBytes, result, fileName);
                    }

                    if (id3v1Tail != null)
                    {
                        ParseId3v1Tail(id3v1Tail, 0, result);
                    }

                    // Если название не прочиталось ни из ID3v2, ни из ID3v1, берем имя файла
                    if (string.IsNullOrEmpty(result.Title))
                    {
                        InferFromFileName(fileName, result);
                    }

                    result.HasBadTags = hasCorruptedId3v1Tail ||
                                        HasSuspiciousNormalizedText(result.Title, fileName) ||
                                        HasSuspiciousNormalizedText(result.Artist, fileName) ||
                                        HasSuspiciousNormalizedText(result.Album, fileName);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AudioMetadataExtractor] Ошибка парсинга тегов для {fileName}: {ex.Message}");
                InferFromFileName(fileName, result);
            }

            return result;
        }

        private static void ParseId3v2Header(byte[] buffer, int length, AudioMetadataResult result, string fileName)
        {
            result.Bitrate = 320; // kbps default for HQ
            result.DurationSeconds = 0; // Определяется точнее или оставляется 0 для нативного подсчета клиентом
            result.AudioFormat = ".mp3";

            try
            {
                int version = buffer[3]; // 2 = ID3v2.2, 3 = ID3v2.3, 4 = ID3v2.4
                byte headerFlags = buffer[5];
                int tagSize = ((buffer[6] & 0x7F) << 21) |
                              ((buffer[7] & 0x7F) << 14) |
                              ((buffer[8] & 0x7F) << 7) |
                              (buffer[9] & 0x7F);

                int maxPos = Math.Min(length, 10 + tagSize);
                int pos = 10;

                // Пропуск Extended Header в ID3v2.3 / ID3v2.4 (флаг 0x40)
                if (version >= 3 && (headerFlags & 0x40) != 0 && pos + 4 <= maxPos)
                {
                    int extSize = version == 4
                        ? ((buffer[pos] & 0x7F) << 21) | ((buffer[pos + 1] & 0x7F) << 14) | ((buffer[pos + 2] & 0x7F) << 7) | (buffer[pos + 3] & 0x7F)
                        : (buffer[pos] << 24) | (buffer[pos + 1] << 16) | (buffer[pos + 2] << 8) | buffer[pos + 3];

                    if (version == 3)
                    {
                        pos += 4 + extSize;
                    }
                    else if (version == 4 && extSize >= 4)
                    {
                        pos += extSize;
                    }
                }

                string? albumArtistFallback = null;
                int frameHeaderSize = version == 2 ? 6 : 10;

                while (pos + frameHeaderSize <= maxPos)
                {
                    if (buffer[pos] == 0) break; // Заполнитель нулями в конце тега

                    string frameId;
                    int frameSize;

                    if (version == 2)
                    {
                        frameId = Encoding.ASCII.GetString(buffer, pos, 3);
                        frameSize = (buffer[pos + 3] << 16) |
                                    (buffer[pos + 4] << 8) |
                                    buffer[pos + 5];
                    }
                    else
                    {
                        frameId = Encoding.ASCII.GetString(buffer, pos, 4);
                        if (version == 4)
                        {
                            frameSize = ((buffer[pos + 4] & 0x7F) << 21) |
                                        ((buffer[pos + 5] & 0x7F) << 14) |
                                        ((buffer[pos + 6] & 0x7F) << 7) |
                                        (buffer[pos + 7] & 0x7F);
                        }
                        else
                        {
                            frameSize = (buffer[pos + 4] << 24) |
                                        (buffer[pos + 5] << 16) |
                                        (buffer[pos + 6] << 8) |
                                        buffer[pos + 7];
                        }
                    }

                    if (frameSize <= 0 || pos + frameHeaderSize + frameSize > maxPos)
                    {
                        break;
                    }

                    int dataOffset = pos + frameHeaderSize;
                    if ((frameId == "APIC" || frameId == "PIC") && result.AlbumCover == null && frameSize > 10)
                    {
                        try
                        {
                            byte apicEncoding = buffer[dataOffset];
                            int p = dataOffset + 1;
                            if (frameId == "PIC")
                            {
                                // ID3v2.2: 3-байтовый формат изображения ("JPG", "PNG")
                                p += 3;
                            }
                            else
                            {
                                while (p < dataOffset + frameSize && buffer[p] != 0) p++;
                                if (p < dataOffset + frameSize) p++; // пропускаем 0 байт mime типа
                            }

                            if (p < dataOffset + frameSize)
                            {
                                p++; // пропускаем байт типа картинки (0x03 front cover и т.д.)
                                if (apicEncoding == 1 || apicEncoding == 2)
                                {
                                    while (p + 1 < dataOffset + frameSize && !(buffer[p] == 0 && buffer[p + 1] == 0)) p += 2;
                                    p += 2;
                                }
                                else
                                {
                                    while (p < dataOffset + frameSize && buffer[p] != 0) p++;
                                    p++;
                                }

                                int imgLen = (dataOffset + frameSize) - p;
                                if (imgLen > 100 && p + imgLen <= length)
                                {
                                    byte[] cover = new byte[imgLen];
                                    Buffer.BlockCopy(buffer, p, cover, 0, imgLen);
                                    result.AlbumCover = cover;
                                }
                            }
                        }
                        catch { }
                    }
                    else if (frameSize > 1)
                    {
                        byte encodingByte = buffer[dataOffset];
                        string textVal = DecodeId3Text(buffer, dataOffset + 1, frameSize - 1, encodingByte);

                        if (!string.IsNullOrWhiteSpace(textVal))
                        {
                            switch (frameId)
                            {
                                case "TIT2":
                                case "TT2":
                                    result.Title = textVal;
                                    break;
                                case "TPE1":
                                case "TP1":
                                    result.Artist = textVal;
                                    break;
                                case "TPE2":
                                case "TP2":
                                    albumArtistFallback ??= textVal;
                                    break;
                                case "TALB":
                                case "TAL":
                                    result.Album = textVal;
                                    break;
                                case "TYER":
                                case "TDRC":
                                case "TYE":
                                    if (int.TryParse(textVal.Length >= 4 ? textVal.Substring(0, 4) : textVal, out int yr))
                                        result.Year = yr;
                                    break;
                                case "TRCK":
                                case "TRK":
                                    var trackPart = textVal.Split('/')[0];
                                    if (int.TryParse(trackPart, out int trk))
                                        result.TrackNumber = trk;
                                    break;
                                case "TCON":
                                case "TCO":
                                    result.Genre = textVal;
                                    break;
                                case "TLEN":
                                case "TLE":
                                    if (int.TryParse(textVal, out int ms) && ms > 0)
                                        result.DurationSeconds = ms / 1000;
                                    break;
                            }
                        }
                    }

                    pos += frameHeaderSize + frameSize;
                }

                if (string.IsNullOrWhiteSpace(result.Artist) && !string.IsNullOrWhiteSpace(albumArtistFallback))
                {
                    result.Artist = albumArtistFallback;
                }
            }
            catch
            {
                // При ошибке парсинга фреймов оставляем то, что успели распарсить
            }

            // Откат на имя файла выполняется единым образом в ExtractFromStream после проверки ID3v2 и ID3v1
        }

        private static void ParseId3v1Tail(byte[] buffer, int offset, AudioMetadataResult result)
        {
            if (offset + 128 > buffer.Length) return;
            if (buffer[offset] != (byte)'T' || buffer[offset + 1] != (byte)'A' || buffer[offset + 2] != (byte)'G')
                return;

            static string ReadId3v1Field(byte[] buf, int fieldOffset, int maxLen)
            {
                int actualLen = 0;
                while (actualLen < maxLen && buf[fieldOffset + actualLen] != 0)
                {
                    actualLen++;
                }
                if (actualLen <= 0) return string.Empty;
                return CleanTagString(DecodeLegacyBytes(buf, fieldOffset, actualLen));
            }

            if (string.IsNullOrEmpty(result.Title))
            {
                string title = ReadId3v1Field(buffer, offset + 3, 30);
                if (!string.IsNullOrEmpty(title)) result.Title = title;
            }
            if (string.IsNullOrEmpty(result.Artist))
            {
                string artist = ReadId3v1Field(buffer, offset + 33, 30);
                if (!string.IsNullOrEmpty(artist)) result.Artist = artist;
            }
            if (string.IsNullOrEmpty(result.Album))
            {
                string album = ReadId3v1Field(buffer, offset + 63, 30);
                if (!string.IsNullOrEmpty(album)) result.Album = album;
            }
            if (!result.Year.HasValue)
            {
                string yearText = ReadId3v1Field(buffer, offset + 93, 4);
                if (int.TryParse(yearText, out int yr) && yr > 0) result.Year = yr;
            }
        }

        private static string DecodeId3Text(byte[] buffer, int offset, int length, byte encodingByte)
        {
            if (length <= 0 || offset < 0 || offset + length > buffer.Length)
                return string.Empty;

            try
            {
                string raw;
                switch (encodingByte)
                {
                    case 1:
                        // UTF-16 с BOM (проверяем явный порядок байт: FE FF = BigEndian, FF FE = LittleEndian)
                        if (length >= 2 && buffer[offset] == 0xFE && buffer[offset + 1] == 0xFF)
                            raw = Encoding.BigEndianUnicode.GetString(buffer, offset + 2, length - 2);
                        else if (length >= 2 && buffer[offset] == 0xFF && buffer[offset + 1] == 0xFE)
                            raw = Encoding.Unicode.GetString(buffer, offset + 2, length - 2);
                        else
                            raw = Encoding.Unicode.GetString(buffer, offset, length);
                        break;
                    case 2:
                        raw = Encoding.BigEndianUnicode.GetString(buffer, offset, length);
                        break;
                    case 3:
                        raw = Encoding.UTF8.GetString(buffer, offset, length);
                        break;
                    default:
                        raw = DecodeLegacyBytes(buffer, offset, length);
                        break;
                }

                return CleanTagString(raw);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Восстанавливает корректную кодировку для строк, прочитанных ATL.NET как ISO-8859-1 (байты 0x00..0xFF).
        /// Не затрагивает строки, которые уже содержат символы вне диапазона Latin1 (например, корректный UTF-8/UTF-16).
        /// </summary>
        private static string? NormalizeTagValue(string? value)
        {
            string cleaned = CleanTagString(value);
            if (string.IsNullOrEmpty(cleaned)) return null;

            if (TemporarilyDisableLegacyEncodingDecoder)
            {
                return cleaned;
            }

            bool hasHighLatin1 = false;
            for (int i = 0; i < cleaned.Length; i++)
            {
                char ch = cleaned[i];
                if (ch > '\u00FF')
                {
                    // Уже декодировано в полноценный Unicode (кириллица, иероглифы и т.п.)
                    return cleaned;
                }
                if (ch >= '\u0080')
                {
                    hasHighLatin1 = true;
                }
            }

            if (!hasHighLatin1)
            {
                return cleaned;
            }

            byte[] rawBytes = Encoding.Latin1.GetBytes(cleaned);
            string repaired = CleanTagString(DecodeLegacyBytes(rawBytes, 0, rawBytes.Length));
            return string.IsNullOrEmpty(repaired) ? null : repaired;
        }

        /// <summary>
        /// Декодирует legacy-байты ID3 (флаг 0x00 / ID3v1):
        /// 1) Проверяет, не записан ли внутри валидный многобайтный UTF-8;
        /// 2) Отличает Windows-1251 от реальной западноевропейской диакритики ISO-8859-1 (Tiësto, Café, Mötley Crüe);
        /// 3) При отсутствии признаков CP1251 сохраняет ISO-8859-1 (Latin1).
        /// </summary>
        private static string DecodeLegacyBytes(byte[] buffer, int offset, int length)
        {
            if (length <= 0) return string.Empty;

            if (TemporarilyDisableLegacyEncodingDecoder)
            {
                return Encoding.Latin1.GetString(buffer, offset, length);
            }

            bool hasNonAscii = false;
            for (int i = 0; i < length; i++)
            {
                if (buffer[offset + i] >= 0x80)
                {
                    hasNonAscii = true;
                    break;
                }
            }

            if (!hasNonAscii)
            {
                return Encoding.ASCII.GetString(buffer, offset, length);
            }

            // 1. Проверка ошибочно помеченного как 0x00 валидного UTF-8
            try
            {
                return StrictUtf8Encoding.GetString(buffer, offset, length);
            }
            catch (DecoderFallbackException)
            {
                // Не UTF-8 — переходим к различению Windows-1251 и ISO-8859-1
            }

            if (LooksLikeWindows1251(buffer, offset, length))
            {
                return Cp1251Encoding.GetString(buffer, offset, length);
            }

            return Encoding.Latin1.GetString(buffer, offset, length);
        }

        private static bool LooksLikeWindows1251(byte[] buffer, int offset, int length)
        {
            int consecutiveCyrillic = 0;
            bool hasC1Printable = false;
            bool hasStandaloneCyrillicToken = false;

            int tokenCyrillicCount = 0;
            bool tokenHasAsciiLetter = false;

            for (int i = 0; i <= length; i++)
            {
                byte b = i < length ? buffer[offset + i] : (byte)0;
                bool isSeparator = i == length || b == 0 || b <= 0x20 ||
                                   (b >= 0x21 && b <= 0x2F) ||
                                   (b >= 0x3A && b <= 0x40) ||
                                   (b >= 0x5B && b <= 0x60) ||
                                   (b >= 0x7B && b <= 0x7F);

                if (isSeparator)
                {
                    if (tokenCyrillicCount > 0 && !tokenHasAsciiLetter)
                    {
                        hasStandaloneCyrillicToken = true;
                    }
                    consecutiveCyrillic = 0;
                    tokenCyrillicCount = 0;
                    tokenHasAsciiLetter = false;
                    continue;
                }

                if ((b >= (byte)'A' && b <= (byte)'Z') || (b >= (byte)'a' && b <= (byte)'z'))
                {
                    tokenHasAsciiLetter = true;
                    consecutiveCyrillic = 0;
                }
                else if (b >= 0xC0 || b == 0xA8 || b == 0xB8)
                {
                    consecutiveCyrillic++;
                    tokenCyrillicCount++;
                    if (consecutiveCyrillic >= 2)
                    {
                        return true;
                    }
                }
                else if (b >= 0x80 && b <= 0x9F)
                {
                    // В ISO-8859-1 диапазон 0x80..0x9F — непечатаемые управляющие символы (C1),
                    // тогда как в Windows-1251 там тире (–, —), кавычки, многоточие и кириллица.
                    hasC1Printable = true;
                    consecutiveCyrillic = 0;
                }
                else
                {
                    consecutiveCyrillic = 0;
                }
            }

            return hasStandaloneCyrillicToken || hasC1Printable;
        }

        private static string CleanTagString(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            if (value.IndexOf('\0') >= 0)
            {
                var parts = value.Split('\0', StringSplitOptions.RemoveEmptyEntries);
                value = parts.Length > 0 ? string.Join(", ", parts) : string.Empty;
            }
            return value.Trim('\0', ' ', '\r', '\n', '\t');
        }

        private static void InferFromFileName(string fileName, AudioMetadataResult result)
        {
            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrEmpty(nameWithoutExt) ||
                nameWithoutExt.StartsWith("allw", StringComparison.OrdinalIgnoreCase) ||
                nameWithoutExt.StartsWith("sync", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".temp", StringComparison.OrdinalIgnoreCase))
            {
                // Не используем мусорные имена временных файлов Allway Sync
                return;
            }

            // Если название в тегах отсутствует, подставляем имя файла без расширения, не затирая уже найденного Artist
            result.Title = nameWithoutExt;
        }

        public static string? FormatTagsSummary(AudioMetadataResult? audioMeta)
        {
            if (audioMeta == null) return null;
            if (string.IsNullOrEmpty(audioMeta.Artist) && string.IsNullOrEmpty(audioMeta.Title)) return null;
            return !string.IsNullOrEmpty(audioMeta.Artist)
                ? $"{audioMeta.Artist} - {audioMeta.Title}"
                : audioMeta.Title;
        }

        private static bool CheckForBadTags(
            string filePath,
            string effectiveFileName,
            string? rawTitle,
            string? rawArtist,
            string? rawAlbum,
            AudioMetadataResult result)
        {
            try
            {
                if (HasSuspiciousRawText(rawTitle) || HasSuspiciousRawText(rawArtist) || HasSuspiciousRawText(rawAlbum))
                    return true;

                if (HasSuspiciousNormalizedText(result.Title, effectiveFileName) ||
                    HasSuspiciousNormalizedText(result.Artist, effectiveFileName) ||
                    HasSuspiciousNormalizedText(result.Album, effectiveFileName))
                    return true;

                if (result.AudioFormat == ".mp3")
                {
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    if (fs.Length >= 128)
                    {
                        byte[] tail = new byte[128];
                        fs.Seek(-128, SeekOrigin.End);
                        int totalRead = 0;
                        while (totalRead < 128)
                        {
                            int r = fs.Read(tail, totalRead, 128 - totalRead);
                            if (r <= 0) break;
                            totalRead += r;
                        }
                        if (totalRead == 128 && HasCorruptedId3v1Tail(tail, 0))
                            return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private static bool HasCorruptedId3v1Tail(byte[] buffer, int offset)
        {
            if (offset + 128 > buffer.Length) return false;
            if (buffer[offset] != (byte)'T' || buffer[offset + 1] != (byte)'A' || buffer[offset + 2] != (byte)'G')
                return false;

            // Поля ID3v1: Title [3..32], Artist [33..62], Album [63..92] (по 30 байт)
            int[] fieldOffsets = { 3, 33, 63 };
            foreach (int fOff in fieldOffsets)
            {
                int firstZero = -1;
                for (int i = 0; i < 30; i++)
                {
                    byte b = buffer[offset + fOff + i];
                    if (b == 0)
                    {
                        firstZero = i;
                        break;
                    }
                    if (b < 0x20)
                    {
                        // Управляющие байты до первого NUL
                        return true;
                    }
                }

                if (firstZero >= 0)
                {
                    for (int j = firstZero + 1; j < 30; j++)
                    {
                        byte b = buffer[offset + fOff + j];
                        // Ненулевой/непробельный мусор памяти после терминатора 0x00 (причина кракозябр в ATL.NET)
                        if (b != 0 && b != 0x20)
                            return true;
                    }
                }
            }

            return false;
        }

        private static bool HasSuspiciousRawText(string? rawValue)
        {
            if (string.IsNullOrEmpty(rawValue)) return false;

            for (int i = 0; i < rawValue.Length; i++)
            {
                char ch = rawValue[i];
                if (ch < '\u0020' && ch != '\0' && ch != '\r' && ch != '\n' && ch != '\t')
                    return true;
                if (ch == '\uFFFD')
                    return true;
                if (char.IsHighSurrogate(ch))
                {
                    if (i + 1 >= rawValue.Length || !char.IsLowSurrogate(rawValue[i + 1]))
                        return true;
                    i++;
                }
                else if (char.IsLowSurrogate(ch))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasSuspiciousNormalizedText(string? value, string? fileName)
        {
            if (string.IsNullOrEmpty(value)) return false;

            int consecutiveHighLatin1 = 0;
            bool hasCjk = false;
            bool hasHiraganaOrKatakana = false;

            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                if (ch < '\u0020' && ch != '\r' && ch != '\n' && ch != '\t')
                    return true;
                if (ch >= '\u0080' && ch <= '\u009F')
                    return true;
                if (ch == '\uFFFD')
                    return true;
                if (ch >= '\uE000' && ch <= '\uF8FF')
                    return true; // Private Use Area (битый UTF-16)
                if (ch == '\u00A4' || ch == '\u00A6' || ch == '\u00AC' || ch == '\u00B8')
                    return true; // Характерные артефакты битых тегов (¤, ¦, ¬, ¸)

                if (ch >= '\u3040' && ch <= '\u30FF')
                    hasHiraganaOrKatakana = true;
                else if ((ch >= '\u4E00' && ch <= '\u9FFF') || (ch >= '\uAC00' && ch <= '\uD7AF'))
                    hasCjk = true;

                if (ch >= '\u00C0' && ch <= '\u00FF')
                {
                    consecutiveHighLatin1++;
                    if (consecutiveHighLatin1 >= 2)
                        return true; // Нераспознанные кракозябры вроде 'Òðåê' или 'ÐŸ'

                    bool prevIsAsciiLetter = i > 0 && ((value[i - 1] >= 'A' && value[i - 1] <= 'Z') || (value[i - 1] >= 'a' && value[i - 1] <= 'z'));
                    bool nextIsAsciiLetter = i + 1 < value.Length && ((value[i + 1] >= 'A' && value[i + 1] <= 'Z') || (value[i + 1] >= 'a' && value[i + 1] <= 'z'));
                    if (!prevIsAsciiLetter && !nextIsAsciiLetter)
                        return true; // Изолированный символ C0..FF без латинского слова (например 'Ï')
                }
                else
                {
                    consecutiveHighLatin1 = 0;
                }
            }

            if (hasCjk && !hasHiraganaOrKatakana && !ContainsCjk(fileName))
                return true;

            return false;
        }

        private static bool ContainsCjk(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char ch in text)
            {
                if ((ch >= '\u3040' && ch <= '\u30FF') ||
                    (ch >= '\u4E00' && ch <= '\u9FFF') ||
                    (ch >= '\uAC00' && ch <= '\uD7AF'))
                    return true;
            }
            return false;
        }
    }

    public class AudioMetadataResult
    {
        public string? Artist { get; set; }
        public string? Title { get; set; }
        public string? Album { get; set; }
        public int? Year { get; set; }
        public string? Genre { get; set; }
        public int? TrackNumber { get; set; }
        public int? DurationSeconds { get; set; }
        public int? Bitrate { get; set; }
        public string? AudioFormat { get; set; } // .mp3, .flac, .m4a
        public byte[]? HeaderCache { get; set; }
        public byte[]? AlbumCover { get; set; }
        public bool HasBadTags { get; set; }
    }
}
