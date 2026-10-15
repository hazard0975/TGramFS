using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TelegramWebDAV.Services
{
    /// <summary>
    /// Ограничивает чтение из родительского потока фиксированным числом байт (длиной части).
    /// При достижении лимита возвращает 0 (EOF), позволяя WTelegramClient корректно завершить загрузку части.
    /// Не закрывает родительский входящий поток при Dispose, благодаря чему следующая часть может читаться дальше.
    /// </summary>
    public class SubStream : Stream
    {
        private readonly Stream _parent;
        private readonly long _maxBytes;
        private long _bytesRead;
        private readonly Action<long, long>? _onProgress;

        public SubStream(Stream parent, long maxBytes, Action<long, long>? onProgress = null)
        {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));
            _maxBytes = maxBytes;
            _bytesRead = 0;
            _onProgress = onProgress;
        }

        public override bool CanRead => _parent.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _maxBytes;

        public override long Position
        {
            get => _bytesRead;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            _parent.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_bytesRead >= _maxBytes) return 0;
            int toRead = (int)Math.Min(count, _maxBytes - _bytesRead);
            int read = _parent.Read(buffer, offset, toRead);
            if (read > 0)
            {
                _bytesRead += read;
                _onProgress?.Invoke(_bytesRead, _maxBytes);
            }
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_bytesRead >= _maxBytes) return 0;
            int toRead = (int)Math.Min(count, _maxBytes - _bytesRead);
            int read = await _parent.ReadAsync(buffer, offset, toRead, cancellationToken);
            if (read > 0)
            {
                _bytesRead += read;
                _onProgress?.Invoke(_bytesRead, _maxBytes);
            }
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_bytesRead >= _maxBytes) return 0;
            int toRead = (int)Math.Min(buffer.Length, _maxBytes - _bytesRead);
            int read = await _parent.ReadAsync(buffer.Slice(0, toRead), cancellationToken);
            if (read > 0)
            {
                _bytesRead += read;
                _onProgress?.Invoke(_bytesRead, _maxBytes);
            }
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            // Намеренно НЕ освобождаем _parent, так как он используется для чтения следующих частей
            base.Dispose(disposing);
        }
    }
}
