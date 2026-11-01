               // Вариант 2: Небольшой файл (<= 1 МБ), сохраненный в памяти
                else if (ctx.SmallFileBuffer != null)
                {
                    long finalLength = ctx.SmallFileBuffer.Length;
                    byte[] fileBytes = ctx.SmallFileBuffer.ToArray();

                    try
                    {
                        if (finalLength <= 1)
                        {
                            byte[]? inlineBytes = finalLength == 1 ? fileBytes : null;
                            _repository.CreateOrUpdateFile(
                                parentId,
                                nodeName,
                                finalLength,
                                tgMessageId: null,
                                tgPreviewMessageId: null,
                                inlineData: inlineBytes,
                                lastModified: targetUpdatedAt,
                                creationDate: targetCreatedAt
                            );

                            node.Size = finalLength;
                            node.InlineData = inlineBytes;
                            node.TgMessageId = null;


                            node.UpdatedAt = targetUpdatedAt;
                            node.CreatedAt = targetCreatedAt;

                            AppLogger.Info("WinFsp", $"Файл '{nodeName}' ({finalLength} байт) сохранен в базе данных без отправки в Telegram (inline_data).");