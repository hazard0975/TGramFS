
[2026-10-25 18:08:16.034] [DEBUG] [WTelegram] 2>Sending   PingDelayDisconnect                      #D2F7
[2026-10-25 18:08:16.078] [DEBUG] [WTelegram] 2>Receiving Pong                                     2026-10-25 16:08:15Z (svc) 
[2026-10-25 18:09:16.070] [DEBUG] [WTelegram] 2>Sending   PingDelayDisconnect                      #22A3
[2026-10-25 18:09:16.112] [DEBUG] [WTelegram] 2>Receiving Pong                                     2026-10-25 16:09:15Z (svc) 
[2026-10-25 18:10:16.118] [DEBUG] [WTelegram] 2>Sending   PingDelayDisconnect                      #D8E7
[2026-10-25 18:10:35.338] [ERROR] [WTelegram] 2>An exception occured in the reactor: System.IO.IOException: Unable to read data from the transport connection: Попытка установить соединение была безуспешной, т.к. от другого компьютера за требуемое время не получен нужный отклик, или было разорвано уже установленное соединение из-за неверного отклика уже подключенного компьютера..
 ---> System.Net.Sockets.SocketException (10060): Попытка установить соединение была безуспешной, т.к. от другого компьютера за требуемое время не получен нужный отклик, или было разорвано уже установленное соединение из-за неверного отклика уже подключенного компьютера.
   --- End of inner exception stack trace ---
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error, CancellationToken cancellationToken)
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource<System.Int32>.GetResult(Int16 token)
   at System.Threading.Tasks.ValueTask`1.ValueTaskSourceAsTask.<>c.<.cctor>b__4_0(Object state)
--- End of stack trace from previous location ---
   at WTelegram.Helpers.FullReadAsync(Stream stream, Byte[] buffer, Int32 length, CancellationToken ct)
   at WTelegram.Client.Reactor(Stream stream, CancellationTokenSource cts)
[2026-10-25 18:10:41.363] [INFO ] [WTelegram] Connecting to 149.154.167.41:443...
[2026-10-25 18:10:41.380] [ERROR] [WTelegram] SocketException HostUnreachable (10065): Сделана попытка выполнить операцию на сокете для недоступного хоста.
[2026-10-25 18:10:41.383] [INFO ] [WTelegram] Connecting to 149.154.167.50:443...
[2026-10-25 18:10:41.388] [INFO ] [WTelegram] Connecting to [2001:67c:4e8:f002::a]:443...
[2026-10-25 18:10:41.389] [WARN ] [TelegramService] [Reactor] Зафиксирован сбой реактора Telegram: Unable to read data from the transport connection: Попытка установить соединение была безуспешной, т.к. от другого компьютера за требуемое время не получен нужный отклик, или было разорвано уже установленное соединение из-за неверного отклика уже подключенного компьютера...
[2026-10-25 18:10:44.386] [INFO ] [TelegramService] [Reactor] Попытка самовосстановления сессии после фатального сбоя реактора...