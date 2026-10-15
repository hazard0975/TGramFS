using System;

namespace TelegramWebDAV.Models
{
    /// <summary>
    /// Представляет часть (сегмент) крупногабаритного файла (> 2 ГБ / 4 ГБ), загруженную в Telegram.
    /// </summary>
    public class NodePart
    {
        public int Id { get; set; }
        public int NodeId { get; set; }
        public int PartIndex { get; set; }
        public long PartSize { get; set; }
        public int TgMessageId { get; set; }
        public long TgChannelId { get; set; }
        public DateTime CreatedAt { get; set; }

        public NodePart() { }

        public NodePart(int nodeId, int partIndex, long partSize, int tgMessageId, long tgChannelId)
        {
            NodeId = nodeId;
            PartIndex = partIndex;
            PartSize = partSize;
            TgMessageId = tgMessageId;
            TgChannelId = tgChannelId;
            CreatedAt = DateTime.UtcNow;
        }
    }
}
