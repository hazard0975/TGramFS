[2026-10-26 03:20:36.969] [DEBUG] [WTelegram] 2>Receiving Pong                                     2026-10-26 01:20:36Z (svc) 
[2026-10-26 03:21:36.966] [DEBUG] [WTelegram] 2>Sending   PingDelayDisconnect                      #7A3B
[2026-10-26 03:21:37.009] [DEBUG] [WTelegram] 2>Receiving Pong                                     2026-10-26 01:21:36Z (svc) 
[2026-10-26 03:22:37.010] [DEBUG] [WTelegram] 2>Sending   PingDelayDisconnect                      #1B93
[2026-10-26 03:22:37.053] [DEBUG] [WTelegram] 2>Receiving Pong                                     2026-10-26 01:22:36Z (svc) 
[2026-10-26 03:23:37.061] [DEBUG] [WTelegram] 2>Sending   PingDelayDisconnect                      #9568
[2026-10-26 03:23:37.105] [DEBUG] [WTelegram] 2>Receiving Pong                                     2026-10-26 01:23:36Z (svc) 
[2026-10-26 03:24:37.119] [DEBUG] [WTelegram] 2>Sending   PingDelayDisconnect                      #52C0
[2026-10-26 03:24:37.162] [DEBUG] [WTelegram] 2>Receiving Pong                                     2026-10-26 01:24:36Z (svc) 
[2026-10-26 03:25:04.777] [DEBUG] [TelegramService] [Network] Зафиксировано изменение сетевых адресов/маршрутов Windows (NetworkAddressChanged).
[2026-10-26 03:25:04.777] [WARN ] [TelegramService] [Reactor] Запуск процедуры самовосстановления сессии (изменение сетевых адресов/маршрутов)...
[2026-10-26 03:25:04.777] [DEBUG] [WTelegram] 2>Sending   Help_GetNearestDc                        #5F87
[2026-10-26 03:25:04.803] [DEBUG] [TelegramService] [Network] Зафиксировано изменение сетевых адресов/маршрутов Windows (NetworkAddressChanged).
[2026-10-26 03:25:04.803] [DEBUG] [TelegramService] [Reactor] Запрос переподключения (изменение сетевых адресов/маршрутов) пропущен: процесс восстановления уже запущен.
[2026-10-26 03:25:05.979] [DEBUG] [TelegramService] [Network] Зафиксировано изменение сетевых адресов/маршрутов Windows (NetworkAddressChanged).
[2026-10-26 03:25:05.979] [DEBUG] [TelegramService] [Reactor] Запрос переподключения (изменение сетевых адресов/маршрутов) пропущен: процесс восстановления уже запущен.
[2026-10-26 03:25:05.988] [DEBUG] [TelegramService] [Network] Зафиксировано изменение сетевых адресов/маршрутов Windows (NetworkAddressChanged).
[2026-10-26 03:25:05.989] [DEBUG] [TelegramService] [Reactor] Запрос переподключения (изменение сетевых адресов/маршрутов) пропущен: процесс восстановления уже запущен.
[2026-10-26 17:22:40.497] [INFO ] [TelegramService] Зафиксирован выход Windows из сна/гибернации (временной скачок: 50261 сек). Ожидание готовности сети...
[2026-10-26 17:22:41.080] [INFO ] [TelegramService] Сетевой интерфейс готов. Проверка связи с Telegram...
[2026-10-26 17:22:41.096] [DEBUG] [WTelegram] 2>Sending   Help_GetNearestDc                        #2754
[2026-10-26 17:22:41.129] [WARN ] [TelegramService] Проверка соединения Telegram после сна выявила сбой сокета: Unable to write data to the transport connection: Программа на вашем хост-компьютере разорвала установленное подключение... Запуск восстановления...
[2026-10-26 17:22:41.129] [DEBUG] [TelegramService] [Reactor] Запрос переподключения (сбой сокета при проверке после сна: Unable to write data to the transport connection: Программа на вашем хост-компьютере разорвала установленное подключение..) пропущен: процесс восстановления уже запущен.
[2026-10-26 17:22:41.143] [ERROR] [WTelegram] 2>An exception occured in the reactor: System.IO.IOException: Unable to read data from the transport connection: Программа на вашем хост-компьютере разорвала установленное подключение..
 ---> System.Net.Sockets.SocketException (10053): Программа на вашем хост-компьютере разорвала установленное подключение.
   --- End of inner exception stack trace ---
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error, CancellationToken cancellationToken)
   at System.Threading.Tasks.ValueTask`1.ValueTaskSourceAsTask.<>c.<.cctor>b__4_0(Object state)
--- End of stack trace from previous location ---
   at WTelegram.Helpers.FullReadAsync(Stream stream, Byte[] buffer, Int32 length, CancellationToken ct)
   at WTelegram.Client.Reactor(Stream stream, CancellationTokenSource cts)
[2026-10-26 17:22:41.252] [WARN ] [TelegramService] [Reactor] Проверочный пинг существующего сокета превысил 5 сек. Переход к пересозданию сессии...
[2026-10-26 17:22:41.256] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-26 17:22:41.849] [DEBUG] [TelegramService] [Network] Зафиксировано изменение сетевых адресов/маршрутов Windows (NetworkAddressChanged).
[2026-10-26 17:22:41.850] [DEBUG] [TelegramService] [Reactor] Запрос переподключения (изменение сетевых адресов/маршрутов) пропущен: процесс восстановления уже запущен.
[2026-10-26 17:22:42.251] [INFO ] [TelegramService] [Reactor] Попытка #1 восстановления связи (изменение сетевых адресов/маршрутов). Подключение через WTelegram...
[2026-10-26 17:22:42.251] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-10-26 17:22:42.251] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-10-26 17:22:42.252] [INFO ] [WTelegram] Loaded previous session
[2026-10-26 17:22:42.253] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-26 17:22:42.253] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-26 17:22:42.254] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-26 17:22:42.254] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-26 17:22:42.255] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-26 17:22:42.258] [ERROR] [TelegramService] Ошибка подключения существующей сессии: Сделана попытка выполнить операцию на сокете для недоступного хоста. | Ex: Сделана попытка выполнить операцию на сокете для недоступного хоста.
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
   at TelegramWebDAV.Services.TelegramService.ConnectAsync(Boolean isSilentRetry)
[2026-10-26 17:22:42.258] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-26 17:22:42.258] [INFO ] [TelegramService] [Reactor] Серверы Telegram пока недоступны (Сделана попытка выполнить операцию на сокете для недоступного хоста.). Ожидание появления сети...
[2026-10-26 17:22:43.808] [DEBUG] [TelegramService] [Network] Зафиксировано изменение сетевых адресов/маршрутов Windows (NetworkAddressChanged).
[2026-10-26 17:22:43.808] [INFO ] [TelegramService] [Network] Системный сигнал Windows: сброс паузы ожидания и немедленная попытка подключения...
[2026-10-26 17:22:43.808] [DEBUG] [TelegramService] [Reactor] Запрос переподключения (изменение сетевых адресов/маршрутов) пропущен: процесс восстановления уже запущен.
[2026-10-26 17:22:43.811] [INFO ] [WTelegram] Loaded previous session
[2026-10-26 17:22:43.811] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-26 17:22:43.811] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-26 17:22:43.812] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-26 17:22:43.812] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-26 17:22:43.813] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-26 17:22:43.813] [DEBUG] [TelegramService] Фоновая попытка подключения сессии к Telegram: Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-26 17:22:43.813] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-26 17:22:43.813] [DEBUG] [TelegramService] [Reactor] Попытка #2 не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-26 17:22:44.946] [WARN ] [TelegramService] [Network] Сетевое подключение Windows разорвано.
[2026-10-26 17:22:44.946] [DEBUG] [TelegramService] [Network] Зафиксировано изменение сетевых адресов/маршрутов Windows (NetworkAddressChanged).
[2026-10-26 17:22:44.946] [INFO ] [TelegramService] [Network] Системный сигнал Windows: сброс паузы ожидания и немедленная попытка подключения...
[2026-10-26 17:22:44.947] [DEBUG] [TelegramService] [Reactor] Запрос переподключения (изменение сетевых адресов/маршрутов) пропущен: процесс восстановления уже запущен.
[2026-10-26 17:22:44.947] [INFO ] [WTelegram] Loaded previous session
[2026-10-26 17:22:44.947] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-26 17:22:44.947] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-26 17:22:44.948] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-26 17:22:44.948] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-26 17:22:44.948] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-26 17:22:44.948] [DEBUG] [TelegramService] Фоновая попытка подключения сессии к Telegram: Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-26 17:22:44.948] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-26 17:22:44.949] [DEBUG] [TelegramService] [Reactor] Попытка #3 не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-26 17:22:44.963] [INFO ] [TelegramService] [Network] Зафиксировано появление активного сетевого подключения Windows.
[2026-10-26 17:22:44.963] [INFO ] [TelegramService] [Network] Системный сигнал Windows: сброс паузы ожидания и немедленная попытка подключения...
[2026-10-26 17:22:44.963] [DEBUG] [TelegramService] [Reactor] Запрос переподключения (восстановление сетевого интерфейса Windows) пропущен: процесс восстановления уже запущен.
[2026-10-26 17:22:44.963] [DEBUG] [TelegramService] [Network] Зафиксировано изменение сетевых адресов/маршрутов Windows (NetworkAddressChanged).
[2026-10-26 17:22:44.964] [DEBUG] [TelegramService] [Reactor] Запрос переподключения (изменение сетевых адресов/маршрутов) пропущен: процесс восстановления уже запущен.
[2026-10-26 17:22:44.964] [INFO ] [WTelegram] Loaded previous session
[2026-10-26 17:22:44.964] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-26 17:22:44.964] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-26 17:22:44.964] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-26 17:22:44.964] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-26 17:22:44.965] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-26 17:22:44.965] [DEBUG] [TelegramService] Фоновая попытка подключения сессии к Telegram: Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-26 17:22:44.965] [INFO ] [WTelegram] 2>Disposing the client
[2026-10-26 17:22:44.965] [DEBUG] [TelegramService] [Reactor] Попытка #4 не удалась: Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-26 17:22:47.031] [DEBUG] [TelegramService] [Network] Зафиксировано изменение сетевых адресов/маршрутов Windows (NetworkAddressChanged).
[2026-10-26 17:22:47.031] [INFO ] [TelegramService] [Network] Системный сигнал Windows: сброс паузы ожидания и немедленная попытка подключения...
[2026-10-26 17:22:47.031] [DEBUG] [TelegramService] [Reactor] Запрос переподключения (изменение сетевых адресов/маршрутов) пропущен: процесс восстановления уже запущен.
[2026-10-26 17:22:47.032] [INFO ] [WTelegram] Loaded previous session
[2026-10-26 17:22:47.032] [DEBUG] [WTelegram] WTelegramClient 4.1.6+e5c6086e1199c947131c2ef44b526ef51501cd67 running under .NET 8.0.32
[2026-10-26 17:22:47.032] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-26 17:22:47.051] [DEBUG] [TelegramService] [Network] Зафиксировано изменение сетевых адресов/маршрутов Windows (NetworkAddressChanged).
[2026-10-26 17:22:47.051] [DEBUG] [TelegramService] [Reactor] Запрос переподключения (изменение сетевых адресов/маршрутов) пропущен: процесс восстановления уже запущен.
[2026-10-26 17:22:47.074] [DEBUG] [WTelegram] 2>Sending   InvokeWithLayer`1                        #1A52
[2026-10-26 17:22:47.205] [DEBUG] [WTelegram] 2>Receiving MsgContainer                             2026-10-26 15:22:47Z (svc)
[2026-10-26 17:22:47.206] [DEBUG] [WTelegram]             → NewSessionCreated                      2026-10-26 15:22:47Z  
[2026-10-26 17:22:47.206] [DEBUG] [WTelegram]             → MsgsAck                                2026-10-26 15:22:47Z (svc) 
[2026-10-26 17:22:47.221] [DEBUG] [WTelegram] 2>Receiving RpcResult                                2026-10-26 15:22:47Z
[2026-10-26 17:22:47.222] [DEBUG] [WTelegram]              → Config                                #1A52
[2026-10-26 17:22:47.222] [INFO ] [WTelegram] Connected to DC 2... default_p2p_contacts, revoke_pm_inbox
[2026-10-26 17:22:47.223] [DEBUG] [WTelegram] 2>Sending   MsgContainer                             2026-10-26 15:22:47Z (svc)
[2026-10-26 17:22:47.223] [DEBUG] [WTelegram]             → Users_GetUsers                         #321E
[2026-10-26 17:22:47.223] [DEBUG] [WTelegram]             → MsgsAck                               
[2026-10-26 17:22:47.279] [DEBUG] [WTelegram] 2>Receiving RpcResult                                2026-10-26 15:22:47Z
[2026-10-26 17:22:47.279] [DEBUG] [WTelegram]              → UserBase[]                            #321E
[2026-10-26 17:22:47.280] [INFO ] [MtprotoWorkerPool] Пул постоянных воркеров запущен: 3 воркера MTProto готовы к обработке очереди.
[2026-10-26 17:22:47.280] [INFO ] [TelegramService] Успешная авторизация пользователя: Vitaly (ID: 317057068)
[2026-10-26 17:22:47.280] [INFO ] [TelegramService] [Reactor] Сессия Telegram успешно восстановлена с попытки #5!
[2026-10-26 17:22:47.280] [DEBUG] [WTelegram] 2>Sending   MsgContainer                             2026-10-26 15:22:47Z (svc)
[2026-10-26 17:22:47.280] [DEBUG] [WTelegram]             → Help_GetConfig                         #6076
[2026-10-26 17:22:47.280] [DEBUG] [WTelegram]             → MsgsAck                               
[2026-10-26 17:22:47.332] [DEBUG] [WTelegram] 2>Receiving RpcResult                                2026-10-26 15:22:47Z
[2026-10-26 17:22:47.332] [DEBUG] [WTelegram]              → Config                                #6076
[2026-10-26 17:22:47.332] [INFO ] [TelegramService] Подключен канал-хранилище из базы SQLite: 'Telegram WebDAV Drive' (ID: 3769686715).
[2026-10-26 17:22:47.801] [DEBUG] [TelegramService] [Network] Зафиксировано изменение сетевых адресов/маршрутов Windows (NetworkAddressChanged).
[2026-10-26 17:22:47.801] [WARN ] [TelegramService] [Reactor] Запуск процедуры самовосстановления сессии (изменение сетевых адресов/маршрутов)...
[2026-10-26 17:22:47.802] [DEBUG] [WTelegram] 2>Sending   MsgContainer                             2026-10-26 15:22:47Z (svc)
[2026-10-26 17:22:47.802] [DEBUG] [WTelegram]             → Help_GetNearestDc                      #587A
[2026-10-26 17:22:47.802] [DEBUG] [WTelegram]             → MsgsAck                               
[2026-10-26 17:22:47.864] [DEBUG] [WTelegram] 2>Receiving RpcResult                                2026-10-26 15:22:47Z
[2026-10-26 17:22:47.864] [DEBUG] [WTelegram]              → NearestDc                             #587A
[2026-10-26 17:22:47.864] [INFO ] [TelegramService] [Reactor] Соединение живо, реактор успешно ответил.
[2026-10-26 17:22:50.072] [DEBUG] [TelegramService] [Network] Зафиксировано изменение сетевых адресов/маршрутов Windows (NetworkAddressChanged).
[2026-10-26 17:22:50.072] [WARN ] [TelegramService] [Reactor] Запуск процедуры самовосстановления сессии (изменение сетевых адресов/маршрутов)...
[2026-10-26 17:22:50.072] [DEBUG] [WTelegram] 2>Sending   MsgContainer                             2026-10-26 15:22:49Z (svc)
[2026-10-26 17:22:50.072] [DEBUG] [WTelegram]             → Help_GetNearestDc                      #4428
[2026-10-26 17:22:50.072] [DEBUG] [WTelegram]             → MsgsAck                               
[2026-10-26 17:22:50.117] [DEBUG] [WTelegram] 2>Receiving RpcResult                                2026-10-26 15:22:49Z
[2026-10-26 17:22:50.117] [DEBUG] [WTelegram]              → NearestDc                             #4428
[2026-10-26 17:22:50.117] [INFO ] [TelegramService] [Reactor] Соединение живо, реактор успешно ответил.
[2026-10-26 17:22:50.336] [DEBUG] [TelegramService] [Network] Зафиксировано изменение сетевых адресов/маршрутов Windows (NetworkAddressChanged).
[2026-10-26 17:22:50.336] [WARN ] [TelegramService] [Reactor] Запуск процедуры самовосстановления сессии (изменение сетевых адресов/маршрутов)...
[2026-10-26 17:22:50.336] [DEBUG] [WTelegram] 2>Sending   MsgContainer                             2026-10-26 15:22:50Z (svc)
[2026-10-26 17:22:50.336] [DEBUG] [WTelegram]             → Help_GetNearestDc                      #0239
[2026-10-26 17:22:50.336] [DEBUG] [WTelegram]             → MsgsAck                               
[2026-10-26 17:22:50.365] [DEBUG] [TelegramService] [Network] Зафиксировано изменение сетевых адресов/маршрутов Windows (NetworkAddressChanged).
[2026-10-26 17:22:50.365] [DEBUG] [TelegramService] [Reactor] Запрос переподключения (изменение сетевых адресов/маршрутов) пропущен: процесс восстановления уже запущен.
[2026-10-26 17:22:50.389] [DEBUG] [WTelegram] 2>Receiving RpcResult                                2026-10-26 15:22:50Z
[2026-10-26 17:22:50.389] [DEBUG] [WTelegram]              → NearestDc                             #0239
[2026-10-26 17:22:50.389] [INFO ] [TelegramService] [Reactor] Соединение живо, реактор успешно ответил.
[2026-10-26 17:22:53.224] [DEBUG] [TelegramService] [Network] Зафиксировано изменение сетевых адресов/маршрутов Windows (NetworkAddressChanged).
[2026-10-26 17:22:53.224] [WARN ] [TelegramService] [Reactor] Запуск процедуры самовосстановления сессии (изменение сетевых адресов/маршрутов)...
[2026-10-26 17:22:53.224] [DEBUG] [WTelegram] 2>Sending   MsgContainer                             2026-10-26 15:22:53Z (svc)
[2026-10-26 17:22:53.224] [DEBUG] [WTelegram]             → Help_GetNearestDc                      #6378
[2026-10-26 17:22:53.224] [DEBUG] [WTelegram]             → MsgsAck                               
[2026-10-26 17:22:53.245] [DEBUG] [TelegramService] [Network] Зафиксировано изменение сетевых адресов/маршрутов Windows (NetworkAddressChanged).
[2026-10-26 17:22:53.245] [DEBUG] [TelegramService] [Reactor] Запрос переподключения (изменение сетевых адресов/маршрутов) пропущен: процесс восстановления уже запущен.
[2026-10-26 17:22:53.419] [DEBUG] [WTelegram] 2>Receiving RpcResult                                2026-10-26 15:22:53Z
[2026-10-26 17:22:53.419] [DEBUG] [WTelegram]              → NearestDc                             #6378
[2026-10-26 17:22:53.419] [INFO ] [TelegramService] [Reactor] Соединение живо, реактор успешно ответил.
[2026-10-26 17:23:47.078] [DEBUG] [WTelegram] 2>Sending   MsgContainer                             2026-10-26 15:23:46Z (svc)
[2026-10-26 17:23:47.078] [DEBUG] [WTelegram]             → PingDelayDisconnect                    #C901
[2026-10-26 17:23:47.078] [DEBUG] [WTelegram]             → MsgsAck                               
[2026-10-26 17:23:47.120] [DEBUG] [WTelegram] 2>Receiving Pong                                     2026-10-26 15:23:46Z (svc) 
[2026-10-26 17:24:30.919] [INFO ] [WinFsp] [ReadDirectoryEntry] Открыта папка 'Root' (ID 1), элементов: 6
[2026-10-26 17:24:34.112] [INFO ] [WinFsp] [ReadDirectoryEntry] Открыта папка 'привязанная' (ID 242), элементов: 2
[2026-10-26 17:24:35.726] [INFO ] [WinFsp] [ReadDirectoryEntry] Открыта папка 'Root' (ID 1), элементов: 6
[2026-10-26 17:24:39.191] [DEBUG] [WTelegram] 2>Sending   Channels_GetMessages                     #1076
[2026-10-26 17:24:39.269] [DEBUG] [WTelegram] 2>Receiving RpcResult                                2026-10-26 15:24:39Z
[2026-10-26 17:24:39.269] [DEBUG] [WTelegram]              → Messages_ChannelMessages              #1076