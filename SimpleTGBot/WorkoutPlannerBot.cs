using System.Threading;

namespace SimpleTGBot;

using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

public class WorkoutPlannerBot
{
    private const string BotToken = "8625533906:AAFkaI-s6ULdJrEENDOKBHkUHh7wAXcpkbQ";
    
    private readonly Dictionary<long, UserState> _userStates = new();
    private readonly Dictionary<long, List<Workout>> _userWorkouts = new();
    private readonly Dictionary<long, UserStats> _userStats = new();
    private readonly ManualResetEvent _shutdownEvent = new(false);
    
    private static readonly string[] WorkoutTypes = 
    {
        "Силовая тренировка",
        "Кардио",
        "Бег",
        "Плавание",
        "Велосипед",
        "Йога",
        "Растяжка",
        "Кроссфит",
        "Бокс",
        "Теннис"
    };

    private enum UserState
    {
        None,
        WaitingForWorkoutName,
        WaitingForWorkoutDay,
        WaitingForWorkoutTime,
        WaitingForStatsPulse,
        WaitingForStatsCalories,
        WaitingForStatsDuration
    }
    
    private record Workout(string Name, string Day, string Time);
    
    private record UserStats(
        int TotalWorkouts = 0,
        int TotalCalories = 0,
        int TotalMinutes = 0,
        int AvgPulse = 0,
        List<int>? PulseHistory = null,
        List<int>? CaloriesHistory = null
    );

    public async Task Run()
    {
        var botClient = new TelegramBotClient(BotToken);
        using CancellationTokenSource cts = new CancellationTokenSource();
        
        ReceiverOptions receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery }
        };

        botClient.StartReceiving(
            updateHandler: HandleUpdate,
            pollingErrorHandler: HandleError,
            receiverOptions: receiverOptions,
            cancellationToken: cts.Token
        );

        var me = await botClient.GetMeAsync(cancellationToken: cts.Token);
        Console.WriteLine($"Бот @{me.Username} запущен.");
        Console.WriteLine("Нажмите Enter для остановки...");
        
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            _shutdownEvent.Set();
        };
        
        _shutdownEvent.WaitOne();
        Console.WriteLine("Бот остановлен.");
    }

    private async Task HandleUpdate(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Message is { } message)
        {
            await HandleMessage(botClient, message, cancellationToken);
        }
        else if (update.CallbackQuery is { } callbackQuery)
        {
            await HandleCallbackQuery(botClient, callbackQuery, cancellationToken);
        }
    }

    private async Task HandleMessage(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        var chatId = message.Chat.Id;
        var text = message.Text;

        if (text == null) return;

        if (!_userWorkouts.ContainsKey(chatId))
        {
            _userWorkouts[chatId] = new List<Workout>();
        }
        
        if (!_userStats.ContainsKey(chatId))
        {
            _userStats[chatId] = new UserStats();
        }

        var currentState = _userStates.GetValueOrDefault(chatId, UserState.None);

        switch (text)
        {
            case "/start":
                await SendMainMenu(botClient, chatId, cancellationToken);
                _userStates[chatId] = UserState.None;
                break;

            case "Добавить тренировку":
                await ShowWorkoutTypes(botClient, chatId, cancellationToken);
                _userStates[chatId] = UserState.WaitingForWorkoutName;
                break;

            case "Мои тренировки":
                await ShowWorkouts(botClient, chatId, cancellationToken);
                break;

            case "Очистить все":
                _userWorkouts[chatId].Clear();
                await botClient.SendTextMessageAsync(chatId, "Все тренировки удалены!", cancellationToken: cancellationToken);
                break;

            case "Добавить статистику":
                await botClient.SendTextMessageAsync(chatId, "Введите пульс (ударов в минуту):", cancellationToken: cancellationToken);
                _userStates[chatId] = UserState.WaitingForStatsPulse;
                break;

            case "Моя статистика":
                await ShowStats(botClient, chatId, cancellationToken);
                break;

            case "Главное меню":
                await SendMainMenu(botClient, chatId, cancellationToken);
                break;

            default:
                await HandleState(botClient, chatId, text, cancellationToken);
                break;
        }
    }

    private async Task HandleState(ITelegramBotClient botClient, long chatId, string text, CancellationToken cancellationToken)
    {
        var currentState = _userStates.GetValueOrDefault(chatId, UserState.None);

        switch (currentState)
        {
            case UserState.WaitingForWorkoutName:
                if (!_userWorkouts.ContainsKey(chatId)) _userWorkouts[chatId] = new List<Workout>();
                _userWorkouts[chatId].Add(new Workout(text, "", ""));
                await botClient.SendTextMessageAsync(chatId, "Выберите день недели:", replyMarkup: GetDaysKeyboard(), cancellationToken: cancellationToken);
                _userStates[chatId] = UserState.WaitingForWorkoutDay;
                break;

            case UserState.WaitingForWorkoutDay:
                var workout = _userWorkouts[chatId].LastOrDefault();
                if (workout != null)
                {
                    _userWorkouts[chatId].Remove(workout);
                    _userWorkouts[chatId].Add(workout with { Day = text });
                    await botClient.SendTextMessageAsync(chatId, "Введите время тренировки (например, 10:00):", cancellationToken: cancellationToken);
                    _userStates[chatId] = UserState.WaitingForWorkoutTime;
                }
                break;

            case UserState.WaitingForWorkoutTime:
                var w = _userWorkouts[chatId].LastOrDefault();
                if (w != null)
                {
                    var workoutTime = text;
                    _userWorkouts[chatId].Remove(w);
                    _userWorkouts[chatId].Add(w with { Time = workoutTime });
                    
                    if (!_userStats.ContainsKey(chatId))
                        _userStats[chatId] = new UserStats();
                    var stats = _userStats[chatId];
                    _userStats[chatId] = stats with { TotalWorkouts = stats.TotalWorkouts + 1 };
                    
                    await botClient.SendTextMessageAsync(chatId, $"Тренировка '{w.Name}' добавлена на {w.Day} в {workoutTime}!", cancellationToken: cancellationToken);
                    _userStates[chatId] = UserState.None;
                }
                break;

            case UserState.WaitingForStatsPulse:
                if (int.TryParse(text, out int pulse) && pulse > 0 && pulse < 250)
                {
                    if (!_userStats.ContainsKey(chatId))
                        _userStats[chatId] = new UserStats();
                    var stats = _userStats[chatId];
                    var newPulseHistory = new List<int>(stats.PulseHistory ?? new List<int>()) { pulse };
                    var newAvgPulse = newPulseHistory.Average();
                    _userStats[chatId] = stats with 
                    { 
                        PulseHistory = newPulseHistory,
                        AvgPulse = (int)newAvgPulse
                    };
                    
                    await botClient.SendTextMessageAsync(chatId, $"Пульс {pulse} сохранён!\nВведите количество сожжённых калорий:", cancellationToken: cancellationToken);
                    _userStates[chatId] = UserState.WaitingForStatsCalories;
                }
                else
                {
                    await botClient.SendTextMessageAsync(chatId, "Введите корректный пульс (1-250):", cancellationToken: cancellationToken);
                }
                break;

            case UserState.WaitingForStatsCalories:
                if (int.TryParse(text, out int calories) && calories >= 0)
                {
                    var stats = _userStats[chatId];
                    var newCaloriesHistory = new List<int>(stats.CaloriesHistory ?? new List<int>()) { calories };
                    _userStats[chatId] = stats with 
                    { 
                        TotalCalories = stats.TotalCalories + calories,
                        CaloriesHistory = newCaloriesHistory
                    };
                    
                    await botClient.SendTextMessageAsync(chatId, $"Калории {calories} сохранены!\nВведите продолжительность тренировки (в минутах):", cancellationToken: cancellationToken);
                    _userStates[chatId] = UserState.WaitingForStatsDuration;
                }
                else
                {
                    await botClient.SendTextMessageAsync(chatId, "Введите корректное количество калорий:", cancellationToken: cancellationToken);
                }
                break;

            case UserState.WaitingForStatsDuration:
                if (int.TryParse(text, out int duration) && duration > 0)
                {
                    var stats = _userStats[chatId];
                    _userStats[chatId] = stats with 
                    { 
                        TotalMinutes = stats.TotalMinutes + duration
                    };
                    
                    await botClient.SendTextMessageAsync(chatId, $"Статистика сохранена!\nТренировка: {duration} минут", cancellationToken: cancellationToken);
                    await ShowStats(botClient, chatId, cancellationToken);
                    _userStates[chatId] = UserState.None;
                }
                else
                {
                    await botClient.SendTextMessageAsync(chatId, "Введите корректную продолжительность (в минутах):", cancellationToken: cancellationToken);
                }
                break;
        }
    }

    private async Task HandleCallbackQuery(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery.Data == null) return;
        
        var chatId = callbackQuery.Message?.Chat.Id;
        if (chatId == null) return;

        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
        
        var currentState = _userStates.GetValueOrDefault(chatId.Value, UserState.None);
        
        if (currentState == UserState.WaitingForWorkoutName)
        {
            if (!_userWorkouts.ContainsKey(chatId.Value)) 
                _userWorkouts[chatId.Value] = new List<Workout>();
            _userWorkouts[chatId.Value].Add(new Workout(callbackQuery.Data, "", ""));
            await botClient.SendTextMessageAsync(chatId, "Выберите день недели:", replyMarkup: GetDaysKeyboard(), cancellationToken: cancellationToken);
            _userStates[chatId.Value] = UserState.WaitingForWorkoutDay;
        }
        else if (currentState == UserState.WaitingForWorkoutDay)
        {
            var workout = _userWorkouts[chatId.Value].LastOrDefault();
            if (workout != null)
            {
                _userWorkouts[chatId.Value].Remove(workout);
                _userWorkouts[chatId.Value].Add(workout with { Day = callbackQuery.Data });
                await botClient.SendTextMessageAsync(chatId, "Введите время тренировки (например, 10:00):", cancellationToken: cancellationToken);
                _userStates[chatId.Value] = UserState.WaitingForWorkoutTime;
            }
        }
    }

    private async Task SendMainMenu(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
    {
        var keyboard = new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton[] { "Добавить тренировку", "Мои тренировки" },
            new KeyboardButton[] { "Добавить статистику", "Моя статистика" },
            new KeyboardButton[] { "Очистить все" }
        })
        {
            ResizeKeyboard = true
        };

        await botClient.SendTextMessageAsync(
            chatId,
            "💪 Планировщик тренировок\n\nВыберите действие:",
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private async Task ShowWorkoutTypes(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
    {
        var keyboard = new InlineKeyboardMarkup(
            WorkoutTypes.Select((type, i) => InlineKeyboardButton.WithCallbackData(type, type))
            .Chunk(2)
            .Select(row => row.ToArray())
            .ToArray()
        );

        await botClient.SendTextMessageAsync(
            chatId,
            "Выберите тип тренировки:",
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private async Task ShowWorkouts(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
    {
        if (!_userWorkouts.TryGetValue(chatId, out var workouts) || workouts.Count == 0)
        {
            await botClient.SendTextMessageAsync(chatId, "У вас пока нет запланированных тренировок.", cancellationToken: cancellationToken);
            return;
        }

        var text = "📅 Ваши тренировки:\n\n" + 
                   string.Join("\n", workouts.Select((w, i) => $"{i + 1}. {w.Name} - {w.Day} в {w.Time}"));
        
        await botClient.SendTextMessageAsync(chatId, text, cancellationToken: cancellationToken);
    }

    private async Task ShowStats(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
    {
        if (!_userStats.TryGetValue(chatId, out var stats) || stats.TotalWorkouts == 0)
        {
            await botClient.SendTextMessageAsync(chatId, "У вас пока нет статистики. Добавьте тренировку!", cancellationToken: cancellationToken);
            return;
        }

        var text = $"📊 Ваша статистика:\n\n" +
                   $"🏋️ Всего тренировок: {stats.TotalWorkouts}\n" +
                   $"🔥 Сожжено калорий: {stats.TotalCalories}\n" +
                   $"⏱️ Общее время: {stats.TotalMinutes} минут\n" +
                   $"❤️ Средний пульс: {stats.AvgPulse} уд/мин";
        
        if (stats.CaloriesHistory?.Count > 0)
        {
            text += $"\n\n📈 История калорий (последние 5):\n" +
                    string.Join("\n", (stats.CaloriesHistory ?? new List<int>()).TakeLast(5).Select((c, i) => $"  Тренировка {i + 1}: {c} ккал"));
        }
        
        await botClient.SendTextMessageAsync(chatId, text, cancellationToken: cancellationToken);
    }

    private static IReplyMarkup GetDaysKeyboard()
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("Понедельник", "Понедельник"), InlineKeyboardButton.WithCallbackData("Вторник", "Вторник") },
            new[] { InlineKeyboardButton.WithCallbackData("Среда", "Среда"), InlineKeyboardButton.WithCallbackData("Четверг", "Четверг") },
            new[] { InlineKeyboardButton.WithCallbackData("Пятница", "Пятница"), InlineKeyboardButton.WithCallbackData("Суббота", "Суббота") },
            new[] { InlineKeyboardButton.WithCallbackData("Воскресенье", "Воскресенье") }
        });
    }

    private Task HandleError(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        var errorMessage = exception switch
        {
            ApiRequestException apiRequest => $"Telegram API Error:\n[{apiRequest.ErrorCode}]\n{apiRequest.Message}",
            _ => exception.ToString()
        };
        Console.WriteLine(errorMessage);
        return Task.CompletedTask;
    }
}