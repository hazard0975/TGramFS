[2026-11-09 02:35:47.751] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-11-09 02:35:47.756] [INFO ] [WTelegram] Loaded previous session
[2026-11-09 02:35:47.756] [INFO ] [WTelegram] Connecting to 149.154.167.51:443...
[2026-11-09 02:35:47.939] [INFO ] [WTelegram] Connected to DC 2... default_p2p_contacts, revoke_pm_inbox
[2026-11-09 02:35:47.994] [WARN ] [WTelegram] A verification code has been sent via App
[2026-11-09 02:35:47.994] [INFO ] [TelegramService] Код подтверждения отправлен в Telegram.
[2026-11-09 02:35:51.039] [INFO ] [TelegramService] [Reactor] Подключение к интернету подтверждено (api.telegram.org доступен). Запуск сессии Telegram...
[2026-11-09 02:35:51.039] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-11-09 02:35:51.039] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-11-09 02:35:51.116] [ERROR] [TelegramService] Ошибка подключения существующей сессии: You must provide a config value for verification_code | Ex: You must provide a config value for verification_code
   at WTelegram.Client.Config(String what)
   at WTelegram.Client.<>c__DisplayClass86_0.<ConfigAsync>b__0()
   at System.Threading.Tasks.Task`1.InnerInvoke()
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
--- End of stack trace from previous location ---
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Threading.Tasks.Task.ExecuteWithThreadLocal(Task& currentTaskSlot, Thread threadPoolThread)
--- End of stack trace from previous location ---
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync(Boolean isSilentRetry)
[2026-11-09 02:35:51.116] [INFO ] [WTelegram] 2>Disposing the client
[2026-11-09 02:35:51.117] [WARN ] [TelegramService] [Reactor] Ошибка сессии Telegram: You must provide a config value for verification_code. Повтор проверки через 3 секунды...
[2026-11-09 02:35:54.161] [INFO ] [TelegramService] [Reactor] Подключение к интернету подтверждено (api.telegram.org доступен). Запуск сессии Telegram...
[2026-11-09 02:35:54.161] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-11-09 02:35:54.161] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-11-09 02:35:54.162] [INFO ] [WTelegram] Loaded previous session
[2026-11-09 02:35:54.162] [INFO ] [WTelegram] Connecting to 149.154.167.51:443...
[2026-11-09 02:35:54.344] [INFO ] [WTelegram] Connected to DC 2... default_p2p_contacts, revoke_pm_inbox
[2026-11-09 02:35:54.399] [WARN ] [WTelegram] A verification code has been sent via App
[2026-11-09 02:35:54.399] [INFO ] [TelegramService] Код подтверждения отправлен в Telegram.
[2026-11-09 02:35:57.444] [INFO ] [TelegramService] [Reactor] Подключение к интернету подтверждено (api.telegram.org доступен). Запуск сессии Telegram...
[2026-11-09 02:35:57.444] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-11-09 02:35:57.444] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-11-09 02:35:57.584] [ERROR] [TelegramService] Ошибка подключения существующей сессии: You must provide a config value for verification_code | Ex: You must provide a config value for verification_code
   at WTelegram.Client.Config(String what)
   at WTelegram.Client.<>c__DisplayClass86_0.<ConfigAsync>b__0()
   at System.Threading.Tasks.Task`1.InnerInvoke()
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
--- End of stack trace from previous location ---
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Threading.Tasks.Task.ExecuteWithThreadLocal(Task& currentTaskSlot, Thread threadPoolThread)
--- End of stack trace from previous location ---
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync(Boolean isSilentRetry)
[2026-11-09 02:35:57.584] [INFO ] [WTelegram] 2>Disposing the client
[2026-11-09 02:35:57.586] [WARN ] [TelegramService] [Reactor] Ошибка сессии Telegram: You must provide a config value for verification_code. Повтор проверки через 3 секунды...
[2026-11-09 02:35:58.440] [INFO ] [WTelegram] Loaded previous session
[2026-11-09 02:35:58.442] [INFO ] [TelegramService] Отправка данных на шаге NeedsCode в Telegram API...
[2026-11-09 02:35:58.442] [INFO ] [WTelegram] Connecting to 149.154.167.51:443...
[2026-11-09 02:35:58.623] [INFO ] [WTelegram] Connected to DC 2... default_p2p_contacts, revoke_pm_inbox
[2026-11-09 02:35:58.671] [ERROR] [WTelegram]              → RpcError 400 PHONE_NUMBER_INVALID     #0126
[2026-11-09 02:35:58.676] [ERROR] [TelegramService] RPC Ошибка: PHONE_NUMBER_INVALID (Код: 400) | Ex: PHONE_NUMBER_INVALID
   at WTelegram.Client.Invoke[T](IMethod`1 query)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.LoginStepAsync(String input)
[2026-11-09 02:36:00.631] [INFO ] [TelegramService] [Reactor] Подключение к интернету подтверждено (api.telegram.org доступен). Запуск сессии Telegram...
[2026-11-09 02:36:00.631] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-11-09 02:36:00.631] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-11-09 02:36:00.633] [ERROR] [TelegramService] Ошибка подключения существующей сессии: PHONE_NUMBER_INVALID | Ex: PHONE_NUMBER_INVALID
   at WTelegram.Client.Invoke[T](IMethod`1 query)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync(Boolean isSilentRetry)
[2026-11-09 02:36:00.633] [INFO ] [WTelegram] 2>Disposing the client
[2026-11-09 02:36:00.634] [WARN ] [TelegramService] [Reactor] Ошибка сессии Telegram: PHONE_NUMBER_INVALID. Повтор проверки через 3 секунды...
[2026-11-09 02:36:03.687] [INFO ] [TelegramService] [Reactor] Подключение к интернету подтверждено (api.telegram.org доступен). Запуск сессии Telegram...
[2026-11-09 02:36:03.687] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-11-09 02:36:03.688] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-11-09 02:36:03.688] [INFO ] [WTelegram] Loaded previous session
[2026-11-09 02:36:03.689] [INFO ] [WTelegram] Connecting to 149.154.167.51:443...
[2026-11-09 02:36:03.872] [INFO ] [WTelegram] Connected to DC 2... default_p2p_contacts, revoke_pm_inbox
[2026-11-09 02:36:03.956] [ERROR] [WTelegram]              → RpcError 400 PHONE_NUMBER_INVALID     #602F
[2026-11-09 02:36:03.958] [ERROR] [TelegramService] Ошибка подключения существующей сессии: PHONE_NUMBER_INVALID | Ex: PHONE_NUMBER_INVALID
   at WTelegram.Client.Invoke[T](IMethod`1 query)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync(Boolean isSilentRetry)
[2026-11-09 02:36:03.958] [INFO ] [WTelegram] 2>Disposing the client
[2026-11-09 02:36:03.960] [WARN ] [TelegramService] [Reactor] Ошибка сессии Telegram: PHONE_NUMBER_INVALID. Повтор проверки через 3 секунды...
[2026-11-09 02:36:07.004] [INFO ] [TelegramService] [Reactor] Подключение к интернету подтверждено (api.telegram.org доступен). Запуск сессии Telegram...
[2026-11-09 02:36:07.004] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-11-09 02:36:07.004] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-11-09 02:36:07.005] [INFO ] [WTelegram] Loaded previous session
[2026-11-09 02:36:07.005] [INFO ] [WTelegram] Connecting to 149.154.167.51:443...
[2026-11-09 02:36:07.188] [INFO ] [WTelegram] Connected to DC 2... default_p2p_contacts, revoke_pm_inbox
[2026-11-09 02:36:07.237] [ERROR] [WTelegram]              → RpcError 400 PHONE_NUMBER_INVALID     #4DAB
[2026-11-09 02:36:07.239] [ERROR] [TelegramService] Ошибка подключения существующей сессии: PHONE_NUMBER_INVALID | Ex: PHONE_NUMBER_INVALID
   at WTelegram.Client.Invoke[T](IMethod`1 query)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync(Boolean isSilentRetry)
[2026-11-09 02:36:07.239] [INFO ] [WTelegram] 2>Disposing the client
[2026-11-09 02:36:07.240] [WARN ] [TelegramService] [Reactor] Ошибка сессии Telegram: PHONE_NUMBER_INVALID. Повтор проверки через 3 секунды...
[2026-11-09 02:36:10.284] [INFO ] [TelegramService] [Reactor] Подключение к интернету подтверждено (api.telegram.org доступен). Запуск сессии Telegram...
[2026-11-09 02:36:10.284] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-11-09 02:36:10.285] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-11-09 02:36:10.286] [INFO ] [WTelegram] Loaded previous session
[2026-11-09 02:36:10.286] [INFO ] [WTelegram] Connecting to 149.154.167.51:443...
[2026-11-09 02:36:10.466] [INFO ] [WTelegram] Connected to DC 2... default_p2p_contacts, revoke_pm_inbox
[2026-11-09 02:36:10.517] [ERROR] [WTelegram]              → RpcError 400 PHONE_NUMBER_INVALID     #D02A
[2026-11-09 02:36:10.519] [ERROR] [TelegramService] Ошибка подключения существующей сессии: PHONE_NUMBER_INVALID | Ex: PHONE_NUMBER_INVALID
   at WTelegram.Client.Invoke[T](IMethod`1 query)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync(Boolean isSilentRetry)
[2026-11-09 02:36:10.519] [INFO ] [WTelegram] 2>Disposing the client
[2026-11-09 02:36:10.521] [WARN ] [TelegramService] [Reactor] Ошибка сессии Telegram: PHONE_NUMBER_INVALID. Повтор проверки через 3 секунды...
[2026-11-09 02:36:13.564] [INFO ] [TelegramService] [Reactor] Подключение к интернету подтверждено (api.telegram.org доступен). Запуск сессии Telegram...
[2026-11-09 02:36:13.564] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-11-09 02:36:13.565] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-11-09 02:36:13.566] [INFO ] [WTelegram] Loaded previous session
[2026-11-09 02:36:13.566] [INFO ] [WTelegram] Connecting to 149.154.167.51:443...
[2026-11-09 02:36:13.742] [INFO ] [WTelegram] Connected to DC 2... default_p2p_contacts, revoke_pm_inbox
[2026-11-09 02:36:13.792] [ERROR] [WTelegram]              → RpcError 400 PHONE_NUMBER_INVALID     #02AD
[2026-11-09 02:36:13.793] [ERROR] [TelegramService] Ошибка подключения существующей сессии: PHONE_NUMBER_INVALID | Ex: PHONE_NUMBER_INVALID
   at WTelegram.Client.Invoke[T](IMethod`1 query)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync(Boolean isSilentRetry)
[2026-11-09 02:36:13.793] [INFO ] [WTelegram] 2>Disposing the client
[2026-11-09 02:36:13.795] [WARN ] [TelegramService] [Reactor] Ошибка сессии Telegram: PHONE_NUMBER_INVALID. Повтор проверки через 3 секунды...
[2026-11-09 02:36:16.838] [INFO ] [TelegramService] [Reactor] Подключение к интернету подтверждено (api.telegram.org доступен). Запуск сессии Telegram...
[2026-11-09 02:36:16.838] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-11-09 02:36:16.839] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-11-09 02:36:16.840] [INFO ] [WTelegram] Loaded previous session
[2026-11-09 02:36:16.840] [INFO ] [WTelegram] Connecting to 149.154.167.51:443...
[2026-11-09 02:36:17.035] [INFO ] [WTelegram] Connected to DC 2... default_p2p_contacts, revoke_pm_inbox
[2026-11-09 02:36:17.085] [ERROR] [WTelegram]              → RpcError 400 PHONE_NUMBER_INVALID     #6361
[2026-11-09 02:36:17.087] [ERROR] [TelegramService] Ошибка подключения существующей сессии: PHONE_NUMBER_INVALID | Ex: PHONE_NUMBER_INVALID
   at WTelegram.Client.Invoke[T](IMethod`1 query)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync(Boolean isSilentRetry)
[2026-11-09 02:36:17.087] [INFO ] [WTelegram] 2>Disposing the client
[2026-11-09 02:36:17.088] [WARN ] [TelegramService] [Reactor] Ошибка сессии Telegram: PHONE_NUMBER_INVALID. Повтор проверки через 3 секунды...
[2026-11-09 02:36:20.131] [INFO ] [TelegramService] [Reactor] Подключение к интернету подтверждено (api.telegram.org доступен). Запуск сессии Telegram...
[2026-11-09 02:36:20.131] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-11-09 02:36:20.131] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-11-09 02:36:20.132] [INFO ] [WTelegram] Loaded previous session
[2026-11-09 02:36:20.132] [INFO ] [WTelegram] Connecting to 149.154.167.51:443...
[2026-11-09 02:36:20.343] [INFO ] [WTelegram] Connected to DC 2... default_p2p_contacts, revoke_pm_inbox
[2026-11-09 02:36:20.392] [ERROR] [WTelegram]              → RpcError 420 FLOOD_WAIT_255           #CD28
[2026-11-09 02:36:20.394] [ERROR] [TelegramService] Ошибка подключения существующей сессии: FLOOD_WAIT_X | Ex: FLOOD_WAIT_X
   at WTelegram.Client.Invoke[T](IMethod`1 query)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync(Boolean isSilentRetry)
[2026-11-09 02:36:20.394] [INFO ] [WTelegram] 2>Disposing the client
[2026-11-09 02:36:20.396] [WARN ] [TelegramService] [Reactor] Ошибка сессии Telegram: FLOOD_WAIT_X. Повтор проверки через 3 секунды...
[2026-11-09 02:36:23.439] [INFO ] [TelegramService] [Reactor] Подключение к интернету подтверждено (api.telegram.org доступен). Запуск сессии Telegram...
[2026-11-09 02:36:23.439] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-11-09 02:36:23.440] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-11-09 02:36:23.441] [INFO ] [WTelegram] Loaded previous session
[2026-11-09 02:36:23.441] [INFO ] [WTelegram] Connecting to 149.154.167.51:443...
[2026-11-09 02:36:23.629] [INFO ] [WTelegram] Connected to DC 2... default_p2p_contacts, revoke_pm_inbox
[2026-11-09 02:36:23.700] [ERROR] [WTelegram]              → RpcError 420 FLOOD_WAIT_252           #9EF7
[2026-11-09 02:36:23.702] [ERROR] [TelegramService] Ошибка подключения существующей сессии: FLOOD_WAIT_X | Ex: FLOOD_WAIT_X
   at WTelegram.Client.Invoke[T](IMethod`1 query)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync(Boolean isSilentRetry)
[2026-11-09 02:36:23.702] [INFO ] [WTelegram] 2>Disposing the client
[2026-11-09 02:36:23.703] [WARN ] [TelegramService] [Reactor] Ошибка сессии Telegram: FLOOD_WAIT_X. Повтор проверки через 3 секунды...
[2026-11-09 02:36:26.748] [INFO ] [TelegramService] [Reactor] Подключение к интернету подтверждено (api.telegram.org доступен). Запуск сессии Telegram...
[2026-11-09 02:36:26.749] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-11-09 02:36:26.749] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-11-09 02:36:26.750] [INFO ] [WTelegram] Loaded previous session
[2026-11-09 02:36:26.750] [INFO ] [WTelegram] Connecting to 149.154.167.51:443...
[2026-11-09 02:36:26.934] [INFO ] [WTelegram] Connected to DC 2... default_p2p_contacts, revoke_pm_inbox
[2026-11-09 02:36:26.985] [ERROR] [WTelegram]              → RpcError 420 FLOOD_WAIT_249           #FD9A
[2026-11-09 02:36:26.986] [ERROR] [TelegramService] Ошибка подключения существующей сессии: FLOOD_WAIT_X | Ex: FLOOD_WAIT_X
   at WTelegram.Client.Invoke[T](IMethod`1 query)
   at WTelegram.Client.LoginUserIfNeeded(CodeSettings settings, Boolean reloginOnFailedResume)
   at WTelegram.Client.RunLoginAsync(String phone)
   at WTelegram.Client.Login(String loginInfo)
   at TelegramWebDAV.Services.TelegramService.ConnectAsync(Boolean isSilentRetry)
[2026-11-09 02:36:26.986] [INFO ] [WTelegram] 2>Disposing the client
[2026-11-09 02:36:26.988] [WARN ] [TelegramService] [Reactor] Ошибка сессии Telegram: FLOOD_WAIT_X. Повтор проверки через 3 секунды...
[2026-11-09 02:36:30.032] [INFO ] [TelegramService] [Reactor] Подключение к интернету подтверждено (api.telegram.org доступен). Запуск сессии Telegram...
[2026-11-09 02:36:30.032] [INFO ] [TelegramService] Проверка сессии и подключение к Telegram...
[2026-11-09 02:36:30.033] [INFO ] [TelegramService] [Streaming Config] Воркеры: 3 | Пейсинг: 180 мс | RAM Кэш: 128 МБ (TTL: 10 мин) | Буфер аудио: 2 МБ | Окно стриминга: 20 МБ | Дисковый кэш: ВЫКЛ (100% RAM)
[2026-11-09 02:36:30.033] [INFO ] [WTelegram] Loaded previous session
[2026-11-09 02:36:30.034] [INFO ] [WTelegram] Connecting to 149.154.167.51:443...
[2026-11-09 02:36:30.206] [INFO ] [WTelegram] Connected to DC 2... default_p2p_contacts, revoke_pm_inbox
[2026-11-09 02:36:30.255] [ERROR] [WTelegram]              → RpcError 420 FLOOD_WAIT_245           #A95E
[2026-11-09 02:36:30.257] [ERROR] [TelegramService] Ошибка подключения существующей сессии: FLOOD_WAIT_X | Ex: FLOOD_WAIT_X