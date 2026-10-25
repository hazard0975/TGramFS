[2026-10-25 22:06:21.282] [INFO ] [WinFsp] [ReadDirectoryEntry] Открыта папка 'Root' (ID 1), элементов: 6
[2026-10-25 22:07:02.650] [DEBUG] [WTelegram] 2>Sending   MsgContainer                             2026-10-25 20:07:01Z (svc)
[2026-10-25 22:07:02.650] [DEBUG] [WTelegram]             → PingDelayDisconnect                    #12D9
[2026-10-25 22:07:02.651] [DEBUG] [WTelegram]             → MsgsAck                               
[2026-10-25 22:07:21.886] [ERROR] [WTelegram] 2>An exception occured in the reactor: System.IO.IOException: Unable to read data from the transport connection: Попытка установить соединение была безуспешной, т.к. от другого компьютера за требуемое время не получен нужный отклик, или было разорвано уже установленное соединение из-за неверного отклика уже подключенного компьютера..
 ---> System.Net.Sockets.SocketException (10060): Попытка установить соединение была безуспешной, т.к. от другого компьютера за требуемое время не получен нужный отклик, или было разорвано уже установленное соединение из-за неверного отклика уже подключенного компьютера.
   --- End of inner exception stack trace ---
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error, CancellationToken cancellationToken)
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource<System.Int32>.GetResult(Int16 token)
   at System.Threading.Tasks.ValueTask`1.ValueTaskSourceAsTask.<>c.<.cctor>b__4_0(Object state)
--- End of stack trace from previous location ---
   at WTelegram.Helpers.FullReadAsync(Stream stream, Byte[] buffer, Int32 length, CancellationToken ct)
   at WTelegram.Client.Reactor(Stream stream, CancellationTokenSource cts)
[2026-10-25 22:07:27.902] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:07:27.904] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:07:27.906] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:07:27.911] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:07:27.912] [WARN ] [TelegramService] [Reactor] Зафиксирован сбой реактора Telegram: Unable to read data from the transport connection: Попытка установить соединение была безуспешной, т.к. от другого компьютера за требуемое время не получен нужный отклик, или было разорвано уже установленное соединение из-за неверного отклика уже подключенного компьютера...
[2026-10-25 22:07:27.914] [WARN ] [TelegramService] [Reactor] Запуск процедуры самовосстановления сессии (сбой реактора TL.ReactorError)...
[2026-10-25 22:07:32.913] [WARN ] [TelegramService] [Reactor] Проверочный пинг существующего сокета превысил 5 сек. Переход к пересозданию сессии...
[2026-10-25 22:07:32.930] [INFO ] [TelegramService] [Reactor] Попытка #1: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:07:32.930] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:07:32.931] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:07:32.931] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:07:32.931] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:07:32.932] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:07:32.932] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:07:32.932] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:07:32.932] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:07:32.933] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:07:32.935] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error, CancellationToken cancellationToken)
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:07:32.935] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:07:32.935] [WARN ] [TelegramService] [Reactor] Попытка #1 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:07:37.958] [INFO ] [TelegramService] [Reactor] Попытка #2: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:07:37.958] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:07:37.959] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:07:37.959] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:07:37.959] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:07:37.959] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:07:37.960] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:07:37.960] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:07:37.960] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:07:37.961] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error, CancellationToken cancellationToken)
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:07:37.961] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:07:37.961] [WARN ] [TelegramService] [Reactor] Попытка #2 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:07:42.976] [INFO ] [TelegramService] [Reactor] Попытка #3: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:07:42.976] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:07:42.976] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:07:42.976] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:07:42.976] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:07:42.976] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:07:42.977] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:07:42.977] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:07:42.977] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:07:42.978] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error, CancellationToken cancellationToken)
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:07:42.978] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:07:42.978] [WARN ] [TelegramService] [Reactor] Попытка #3 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:07:48.006] [INFO ] [TelegramService] [Reactor] Попытка #4: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:07:48.006] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:07:48.006] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:07:48.010] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:07:48.010] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:07:48.010] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:07:48.010] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:07:48.011] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:07:48.011] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:07:48.012] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error, CancellationToken cancellationToken)
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:07:48.012] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:07:48.012] [WARN ] [TelegramService] [Reactor] Попытка #4 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:07:53.027] [INFO ] [TelegramService] [Reactor] Попытка #5: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:07:53.027] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:07:53.027] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:07:53.028] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:07:53.028] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:07:53.028] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:07:53.028] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:07:53.028] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:07:53.029] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:07:53.029] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error, CancellationToken cancellationToken)
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:07:53.029] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:07:53.029] [WARN ] [TelegramService] [Reactor] Попытка #5 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:07:58.048] [INFO ] [TelegramService] [Reactor] Попытка #6: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:07:58.048] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:07:58.048] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:07:58.049] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:07:58.049] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:07:58.049] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:07:58.049] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:07:58.049] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:07:58.049] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:07:58.050] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error, CancellationToken cancellationToken)
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:07:58.050] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:07:58.050] [WARN ] [TelegramService] [Reactor] Попытка #6 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:08:03.075] [INFO ] [TelegramService] [Reactor] Попытка #7: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:08:03.075] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:08:03.075] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:08:03.075] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:08:03.075] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:08:03.075] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:08:03.076] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:08:03.076] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:08:03.076] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:08:03.077] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error, CancellationToken cancellationToken)
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:08:03.077] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:08:03.077] [WARN ] [TelegramService] [Reactor] Попытка #7 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:08:08.104] [INFO ] [TelegramService] [Reactor] Попытка #8: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:08:08.104] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:08:08.104] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:08:08.104] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:08:08.104] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:08:08.104] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:08:08.105] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:08:08.105] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:08:08.105] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:08:08.106] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error, CancellationToken cancellationToken)
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:08:08.106] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:08:08.106] [WARN ] [TelegramService] [Reactor] Попытка #8 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:08:13.135] [INFO ] [TelegramService] [Reactor] Попытка #9: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:08:13.135] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:08:13.135] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:08:13.136] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:08:13.136] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:08:13.136] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:08:13.136] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:08:13.136] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:08:13.136] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:08:13.137] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error, CancellationToken cancellationToken)
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:08:13.137] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:08:13.137] [WARN ] [TelegramService] [Reactor] Попытка #9 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:08:18.183] [INFO ] [TelegramService] [Reactor] Попытка #10: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:08:18.183] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:08:18.183] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:08:18.184] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:08:18.184] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:08:18.184] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:08:18.184] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:08:18.184] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:08:18.184] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:08:18.185] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error, CancellationToken cancellationToken)
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:08:18.185] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:08:18.185] [WARN ] [TelegramService] [Reactor] Попытка #10 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:08:23.200] [INFO ] [TelegramService] [Reactor] Попытка #11: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:08:23.201] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:08:23.201] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:08:23.201] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:08:23.201] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:08:23.201] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:08:23.202] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:08:23.202] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:08:23.202] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:08:23.202] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error, CancellationToken cancellationToken)
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:08:23.202] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:08:23.203] [WARN ] [TelegramService] [Reactor] Попытка #11 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:08:28.226] [INFO ] [TelegramService] [Reactor] Попытка #12: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:08:28.226] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:08:28.226] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:08:28.226] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:08:28.226] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:08:28.226] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:08:28.227] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:08:28.227] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:08:28.227] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:08:28.228] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error, CancellationToken cancellationToken)
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:08:28.228] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:08:28.228] [WARN ] [TelegramService] [Reactor] Попытка #12 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:08:33.252] [INFO ] [TelegramService] [Reactor] Попытка #13: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:08:33.252] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:08:33.252] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:08:33.253] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:08:33.253] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:08:33.253] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:08:33.253] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:08:33.253] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:08:33.253] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:08:33.254] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error, CancellationToken cancellationToken)
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:08:33.254] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:08:33.254] [WARN ] [TelegramService] [Reactor] Попытка #13 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:08:38.287] [INFO ] [TelegramService] [Reactor] Попытка #14: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:08:38.287] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:08:38.288] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:08:38.288] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:08:38.288] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:08:38.288] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:08:38.288] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:08:38.288] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:08:38.289] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:08:38.289] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error, CancellationToken cancellationToken)
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:08:38.289] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:08:38.289] [WARN ] [TelegramService] [Reactor] Попытка #14 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:08:43.307] [INFO ] [TelegramService] [Reactor] Попытка #15: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:08:43.307] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:08:43.307] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:08:43.308] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:08:43.308] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:08:43.308] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:08:43.308] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:08:43.308] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:08:43.308] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:08:43.309] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error, CancellationToken cancellationToken)
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:08:43.309] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:08:43.309] [WARN ] [TelegramService] [Reactor] Попытка #15 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:08:48.335] [INFO ] [TelegramService] [Reactor] Попытка #16: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:08:48.335] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:08:48.335] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:08:48.335] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:08:48.335] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:08:48.335] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:08:48.336] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:08:48.336] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:08:48.336] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:08:48.337] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error, CancellationToken cancellationToken)
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:08:48.337] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:08:48.337] [WARN ] [TelegramService] [Reactor] Попытка #16 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:08:53.362] [INFO ] [TelegramService] [Reactor] Попытка #17: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:08:53.362] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:08:53.362] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:08:53.362] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:08:53.362] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:08:53.362] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:08:53.363] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:08:53.363] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:08:53.363] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:08:53.364] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error, CancellationToken cancellationToken)
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:08:53.364] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:08:53.364] [WARN ] [TelegramService] [Reactor] Попытка #17 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:08:58.390] [INFO ] [TelegramService] [Reactor] Попытка #18: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:08:58.390] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:08:58.390] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:08:58.391] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:08:58.391] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:08:58.391] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:08:58.391] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:08:58.391] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:08:58.391] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:08:58.393] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error, CancellationToken cancellationToken)
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:08:58.393] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:08:58.393] [WARN ] [TelegramService] [Reactor] Попытка #18 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:09:03.421] [INFO ] [TelegramService] [Reactor] Попытка #19: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:09:03.421] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:09:03.421] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:09:03.422] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:09:03.422] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:09:03.422] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:09:03.422] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:09:03.422] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:09:03.422] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:09:03.423] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error, CancellationToken cancellationToken)
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:09:03.423] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:09:03.423] [WARN ] [TelegramService] [Reactor] Попытка #19 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:09:08.452] [INFO ] [TelegramService] [Reactor] Попытка #20: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:09:08.452] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:09:08.452] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:09:08.452] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:09:08.452] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:09:08.452] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:09:08.453] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:09:08.453] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:09:08.453] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:09:08.454] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:09:08.454] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:09:08.454] [WARN ] [TelegramService] [Reactor] Попытка #20 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:09:13.487] [INFO ] [TelegramService] [Reactor] Попытка #21: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:09:13.487] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:09:13.487] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:09:13.487] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:09:13.488] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:09:13.488] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:09:13.488] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:09:13.488] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:09:13.488] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:09:13.489] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:09:13.489] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:09:13.489] [WARN ] [TelegramService] [Reactor] Попытка #21 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:09:18.509] [INFO ] [TelegramService] [Reactor] Попытка #22: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:09:18.509] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:09:18.509] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:09:18.510] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:09:18.510] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:09:18.510] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:09:18.510] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:09:18.510] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:09:18.510] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:09:18.511] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:09:18.511] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:09:18.511] [WARN ] [TelegramService] [Reactor] Попытка #22 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:09:23.536] [INFO ] [TelegramService] [Reactor] Попытка #23: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:09:23.536] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:09:23.536] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:09:23.536] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:09:23.536] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:09:23.536] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:09:23.537] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:09:23.537] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:09:23.537] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:09:23.538] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:09:23.538] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:09:23.538] [WARN ] [TelegramService] [Reactor] Попытка #23 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:09:28.564] [INFO ] [TelegramService] [Reactor] Попытка #24: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:09:28.564] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:09:28.564] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:09:28.564] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:09:28.564] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:09:28.564] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:09:28.565] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:09:28.565] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:09:28.565] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:09:28.566] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:09:28.566] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:09:28.566] [WARN ] [TelegramService] [Reactor] Попытка #24 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:09:33.593] [INFO ] [TelegramService] [Reactor] Попытка #25: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:09:33.593] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:09:33.593] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:09:33.593] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:09:33.593] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:09:33.593] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:09:33.594] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:09:33.594] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:09:33.594] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:09:33.595] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:09:33.595] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:09:33.595] [WARN ] [TelegramService] [Reactor] Попытка #25 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:09:38.615] [INFO ] [TelegramService] [Reactor] Попытка #26: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:09:38.615] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:09:38.615] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:09:38.616] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:09:38.616] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:09:38.616] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:09:38.616] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:09:38.616] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:09:38.616] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:09:38.617] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:09:38.617] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:09:38.617] [WARN ] [TelegramService] [Reactor] Попытка #26 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:09:43.655] [INFO ] [TelegramService] [Reactor] Попытка #27: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:09:43.655] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:09:43.655] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:09:43.656] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:09:43.656] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:09:43.656] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:09:43.656] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:09:43.656] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:09:43.656] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:09:43.657] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:09:43.657] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:09:43.657] [WARN ] [TelegramService] [Reactor] Попытка #27 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:09:48.671] [INFO ] [TelegramService] [Reactor] Попытка #28: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:09:48.671] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:09:48.672] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:09:48.672] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:09:48.672] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:09:48.672] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:09:48.672] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:09:48.672] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:09:48.673] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:09:48.673] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:09:48.673] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:09:48.673] [WARN ] [TelegramService] [Reactor] Попытка #28 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:09:53.700] [INFO ] [TelegramService] [Reactor] Попытка #29: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:09:53.700] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:09:53.700] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:09:53.700] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:09:53.700] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:09:53.700] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:09:53.701] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:09:53.701] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:09:53.701] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:09:53.702] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:09:53.702] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:09:53.702] [WARN ] [TelegramService] [Reactor] Попытка #29 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:09:58.728] [INFO ] [TelegramService] [Reactor] Попытка #30: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:09:58.728] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:09:58.728] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:09:58.729] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:09:58.729] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:09:58.729] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:09:58.729] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 22:09:58.729] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 22:09:58.729] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 22:09:58.730] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Sockets.TcpClient.CompleteConnectAsync(ValueTask task)
   at TelegramWebDAV.Services.TelegramService.SafeTcpHandlerAsync(String host, Int32 port)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.DoConnectAsync(Boolean quickResume)
   at WTelegram.Client.ConnectAsync(Boolean quickResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync()
[2026-10-25 22:09:58.730] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-25 22:09:58.730] [WARN ] [TelegramService] [Reactor] Попытка #30 переподключения не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.. Ожидание 5 сек...
[2026-10-25 22:10:03.759] [INFO ] [TelegramService] [Reactor] Попытка #31: Сеть Windows доступна. Пересоздание клиента WTelegram и переподключение...
[2026-10-25 22:10:03.759] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-25 22:10:03.760] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-25 22:10:03.760] [INFO ] [WTelegram] Loaded previous session
[2026-10-25 22:10:03.760] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-25 22:10:03.760] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 22:10:03.801] [DEBUG] [WTelegram] 2>Sending   InvokeWithLayer`1                        #6BE6
[2026-10-25 22:10:03.929] [DEBUG] [WTelegram] 2>Receiving MsgContainer                             2026-10-25 20:10:03Z (svc)
[2026-10-25 22:10:03.929] [DEBUG] [WTelegram]             → RpcResult                              2026-10-25 20:06:03Z
[2026-10-25 22:10:03.929] [DEBUG] [WTelegram]              → Config                                for past msgId #62DE
[2026-10-25 22:10:03.929] [DEBUG] [WTelegram]             → MsgsAck                                2026-10-25 20:10:03Z (svc) 
[2026-10-25 22:10:03.944] [DEBUG] [WTelegram] 2>Receiving RpcResult                                2026-10-25 20:10:04Z
[2026-10-25 22:10:03.944] [DEBUG] [WTelegram]              → Config                                #6BE6
[2026-10-25 22:10:03.944] [INFO ] [WTelegram] Connected to DC 2... default_p2p_contacts, revoke_pm_inbox
[2026-10-25 22:10:03.944] [DEBUG] [WTelegram] 2>Sending   MsgContainer                             2026-10-25 20:10:03Z (svc)
[2026-10-25 22:10:03.945] [DEBUG] [WTelegram]             → Users_GetUsers                         #058F
[2026-10-25 22:10:03.945] [DEBUG] [WTelegram]             → MsgsAck                               
[2026-10-25 22:10:03.992] [DEBUG] [WTelegram] 2>Receiving RpcResult                                2026-10-25 20:10:03Z
[2026-10-25 22:10:03.992] [DEBUG] [WTelegram]              → UserBase[]                            #058F
[2026-10-25 22:10:03.993] [INFO ] [MtprotoWorkerPool] Пул постоянных воркеров запущен: 3 воркера MTProto готовы к обработке очереди.
[2026-10-25 22:10:03.993] [INFO ] [TelegramService] Успешная авторизация пользователя: Vitaly (ID: 317057068)
[2026-10-25 22:10:03.993] [INFO ] [TelegramService] [Reactor] Сессия Telegram успешно восстановлена с попытки #31!
[2026-10-25 22:10:03.994] [DEBUG] [WTelegram] 2>Sending   MsgContainer                             2026-10-25 20:10:03Z (svc)
[2026-10-25 22:10:03.994] [DEBUG] [WTelegram]             → Help_GetConfig                         #673F
[2026-10-25 22:10:03.994] [DEBUG] [WTelegram]             → MsgsAck                               
[2026-10-25 22:10:04.043] [DEBUG] [WTelegram] 2>Receiving RpcResult                                2026-10-25 20:10:03Z
[2026-10-25 22:10:04.043] [DEBUG] [WTelegram]              → Config                                #673F
[2026-10-25 22:10:04.043] [INFO ] [TelegramService] Подключен канал-хранилище из базы SQLite: 'Telegram WebDAV Drive' (ID: 3769686715).