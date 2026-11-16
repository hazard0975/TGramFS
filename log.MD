
[2026-11-17 01:01:21.813] [INFO ] [WinFsp] Перманентное удаление элемента '2004-2005' (ID 10) из корзины...
[2026-11-17 01:01:21.873] [INFO ] [Database] [EnqueuePermanentDeletion] Транзакция перманентного удаления: 137 узлов из БД, 132 уникальных сообщений Telegram в очередь pending_deletions.
[2026-11-17 01:01:22.364] [ERROR] [Database] [EnqueuePermanentDeletion] Сбой транзакции перманентного удаления: SQLite Error 19: 'FOREIGN KEY constraint failed'. | Ex: SQLite Error 19: 'FOREIGN KEY constraint failed'.
   at Microsoft.Data.Sqlite.SqliteException.ThrowExceptionForRC(Int32 rc, sqlite3 db)
   at Microsoft.Data.Sqlite.SqliteDataReader.NextResult()
   at Microsoft.Data.Sqlite.SqliteCommand.ExecuteReader(CommandBehavior behavior)
   at Microsoft.Data.Sqlite.SqliteCommand.ExecuteNonQuery()
   at TGramFS.Database.NodeRepository.EnqueuePermanentDeletion(List`1 items, List`1 dbNodeIds)
[2026-11-17 01:01:22.369] [INFO ] [WinFsp] [ReadDirectoryEntry] Открыта папка '.Trash' (/.Trash) (ID 2), элементов: 1
[2026-11-17 01:01:22.387] [INFO ] [WinFsp] Перманентное удаление элемента 'копирование' (ID 213) из корзины...
[2026-11-17 01:01:22.391] [INFO ] [Database] [EnqueuePermanentDeletion] Транзакция перманентного удаления: 79 узлов из БД, 101 уникальных сообщений Telegram в очередь pending_deletions.
[2026-11-17 01:01:22.395] [ERROR] [Database] [EnqueuePermanentDeletion] Сбой транзакции перманентного удаления: SQLite Error 19: 'FOREIGN KEY constraint failed'. | Ex: SQLite Error 19: 'FOREIGN KEY constraint failed'.
   at Microsoft.Data.Sqlite.SqliteException.ThrowExceptionForRC(Int32 rc, sqlite3 db)
   at Microsoft.Data.Sqlite.SqliteDataReader.NextResult()
   at Microsoft.Data.Sqlite.SqliteCommand.ExecuteReader(CommandBehavior behavior)
   at Microsoft.Data.Sqlite.SqliteCommand.ExecuteNonQuery()
   at TGramFS.Database.NodeRepository.EnqueuePermanentDeletion(List`1 items, List`1 dbNodeIds)
[2026-11-17 01:01:22.419] [INFO ] [WinFsp] Перманентное удаление элемента 'перенос' (ID 192) из корзины...
[2026-11-17 01:01:22.423] [INFO ] [Database] [EnqueuePermanentDeletion] Транзакция перманентного удаления: 57 узлов из БД, 66 уникальных сообщений Telegram в очередь pending_deletions.
[2026-11-17 01:01:22.426] [ERROR] [Database] [EnqueuePermanentDeletion] Сбой транзакции перманентного удаления: SQLite Error 19: 'FOREIGN KEY constraint failed'. | Ex: SQLite Error 19: 'FOREIGN KEY constraint failed'.
   at Microsoft.Data.Sqlite.SqliteException.ThrowExceptionForRC(Int32 rc, sqlite3 db)
   at Microsoft.Data.Sqlite.SqliteDataReader.NextResult()
   at Microsoft.Data.Sqlite.SqliteCommand.ExecuteReader(CommandBehavior behavior)
   at Microsoft.Data.Sqlite.SqliteCommand.ExecuteNonQuery()
   at TGramFS.Database.NodeRepository.EnqueuePermanentDeletion(List`1 items, List`1 dbNodeIds)
[2026-11-17 01:01:22.448] [INFO ] [WinFsp] Перманентное удаление элемента 'ц' (ID 171) из корзины...
[2026-11-17 01:01:22.449] [INFO ] [Database] [EnqueuePermanentDeletion] Транзакция перманентного удаления: 1 узлов из БД, 0 уникальных сообщений Telegram в очередь pending_deletions.
[2026-11-17 01:01:22.720] [INFO ] [Database] [EnqueuePermanentDeletion] Транзакция успешно зафиксирована.
[2026-11-17 01:01:22.721] [INFO ] [WinFsp] Удалено 1 узлов из базы; 0 сообщений отправлено в очередь удаления Telegram.
[2026-11-17 01:01:22.734] [INFO ] [WinFsp] Перманентное удаление элемента 'Alloise - Love Me Or Leave Me (Mokka China & Vitaly Hazard remix).mp4' (ID 347) из корзины...
[2026-11-17 01:01:22.734] [INFO ] [Database] [EnqueuePermanentDeletion] Транзакция перманентного удаления: 1 узлов из БД, 1 уникальных сообщений Telegram в очередь pending_deletions.
[2026-11-17 01:01:22.867] [INFO ] [Database] [EnqueuePermanentDeletion] Транзакция успешно зафиксирована.
[2026-11-17 01:01:22.868] [INFO ] [WinFsp] Удалено 1 узлов из базы; 1 сообщений отправлено в очередь удаления Telegram.
[2026-11-17 01:01:22.869] [INFO ] [TelegramService] [DeletionQueue] Фоновое перманентное удаление 1 сообщений из Telegram...