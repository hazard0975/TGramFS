using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TelegramWebDAV.Services
{
    /// <summary>
    /// Ограничивающий потоковый адаптер (Bounded Sub-Stream).
    /// Оборачивает нижележащий поток и читает из него ровно bytesToRead байт,
    /// сообщая клиенту (WTelegramClient) строго фиксированный Length = bytesToRead
    /// и возвращая EOF (0 байт), когда данный сегмент полностью прочитан.
    /// Позволяет передавать крупные потоки произвольного размера в MTProto последовательными частями
    /// без буферизации на диск и без перемотки назад.
    /// </summary>
    public class BoundedStream : Stream
    {
        private readonly Stream _innerStream;
        private readonly long _length;
        private long _position;
        private readonly Action<long, long>? _onProgress;

        public BoundedStream(Stream innerStream, long length, Action<long, long>? onProgress = null)
        {
            _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
            _length = length;
            _position = 0;
            _onProgress = onProgress;
        }

        public override bool CanRead => _innerStream.CanRead;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set
            {
                if (value == _position) return;
                if (value == 0 && _position == 0) return;
                throw new NotSupportedException($"BoundedStream поддерживает только последовательное прямое чтение (запрошено: {value}, текущее: {_position}).");
            }
        }

        public override void Flush() => _innerStream.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException();

            if (_position >= _length) return 0; // Достигнут конец этой части

            int toRead = (int)Math.Min(count, _length - _position);
            int bytesRead = _innerStream.Read(buffer, offset, toRead);
            if (bytesRead > 0)
            {
                _position += bytesRead;
                _onProgress?.Invoke(_position, _length);
            }
            return bytesRead;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException();

            if (_position >= _length) return 0;

            int toRead = (int)Math.Min(count, _length - _position);
            int bytesRead = await _innerStream.ReadAsync(buffer, offset, toRead, cancellationToken);
            if (bytesRead > 0)
            {
                _position += bytesRead;
                _onProgress?.Invoke(_position, _length);
            }
            return bytesRead;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= _length) return 0;

            int toRead = (int)Math.Min(buffer.Length, _length - _position);
            var slice = buffer.Slice(0, toRead);
            int bytesRead = await _innerStream.ReadAsync(slice, cancellationToken);
            if (bytesRead > 0)
            {
                _position += bytesRead;
                _onProgress?.Invoke(_position, _length);
            }
            return bytesRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long targetPosition = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };

            if (targetPosition == _position) return _position;
            if (origin == SeekOrigin.Begin && offset == 0 && _position == 0) return 0;

            throw new NotSupportedException($"BoundedStream поддерживает только последовательное прямое чтение (попытка Seek с {_position} на {targetPosition}).");
        }

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            // Не освобождаем _innerStream, так как родительский поток передается в следующие части
            base.Dispose(disposing);
        }
    }
}
