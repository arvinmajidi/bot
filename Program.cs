using System.Text;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramExpenseBot
{
    class UserSession
    {
        public string CurrentName { get; set; }
        public bool WaitingForExpense { get; set; } = false;
        public Dictionary<string, decimal> Expenses { get; set; } = new Dictionary<string, decimal>();
    }

    class Program
    {
        static TelegramBotClient botClient;
        static Dictionary<long, UserSession> sessions = new Dictionary<long, UserSession>();

        static async Task Main(string[] args)
        {
            botClient = new TelegramBotClient("8497857507:AAG34Ijs0XMZwzJDhKnnLdqbMr1PaiD2qFU"); // جایگزین توکن خودت

            // نسخه‌های جدید GetMe همزمان است
            var me = botClient.GetMe();
            Console.WriteLine($"ربات روشن شد.");

            var cts = new CancellationTokenSource();
            botClient.StartReceiving(
                HandleUpdateAsync,
                HandleErrorAsync,
                new ReceiverOptions() { AllowedUpdates = { } },
                cancellationToken: cts.Token
            );

            Console.WriteLine("برای خروج Enter را بزنید.");
            Console.ReadLine();
            cts.Cancel();
        }

        static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (update.Type == UpdateType.Message)
            {
                await HandleMessage(botClient, update.Message);
            }
            else if (update.Type == UpdateType.CallbackQuery)
            {
                await HandleCallback(botClient, update.CallbackQuery);
            }
        }

        static async Task HandleMessage(ITelegramBotClient botClient, Message message)
        {
            if (message.Text == null) return;
            long chatId = message.Chat.Id;

            if (!sessions.ContainsKey(chatId))
                sessions[chatId] = new UserSession();

            var session = sessions[chatId];

            // فرمان /start
            if (message.Text.StartsWith("/start"))
            {
                session.Expenses.Clear();
                session.WaitingForExpense = false;
                await botClient.SendMessage(chatId,
                    "سلام! اسم اولین نفر را وارد کن:");
                return;
            }

            // اگر منتظر عدد خرج هستیم
            if (session.WaitingForExpense)
            {
                if (decimal.TryParse(message.Text, out decimal expense))
                {
                    session.Expenses[session.CurrentName] = expense;
                    session.WaitingForExpense = false;

                    // دکمه بله/خیر برای اضافه کردن نفر بعد
                    var keyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("بله", "yes"),
                            InlineKeyboardButton.WithCallbackData("خیر", "no")
                        }
                    });

                    await botClient.SendMessage(chatId,
                        $"ثبت شد: {session.CurrentName} → {expense:N0}\nمی‌خوای نفر بعد را اضافه کنی؟",
                        replyMarkup: keyboard);
                }
                else
                {
                    await botClient.SendMessage(chatId, "لطفاً عدد معتبر وارد کن:");
                }
                return;
            }

            // اگر منتظر اسم هستیم
            session.CurrentName = message.Text.Trim();
            session.WaitingForExpense = true;
            await botClient.SendMessage(chatId, $"خرج {session.CurrentName} را وارد کن:");
        }

        static async Task HandleCallback(ITelegramBotClient botClient, Telegram.Bot.Types.CallbackQuery callback)
        {
            long chatId = callback.Message.Chat.Id;

            if (!sessions.ContainsKey(chatId))
                sessions[chatId] = new UserSession();

            var session = sessions[chatId];

            if (callback.Data == "yes")
            {
                await botClient.SendMessage(chatId, "اسم نفر بعد را وارد کن:");
            }
            else if (callback.Data == "no")
            {
                string result = CalculateSettlement(session.Expenses);
                await botClient.SendMessage(chatId, result);
                session.Expenses.Clear();
            }

            // پاسخ به CallbackQuery ضروری است
            await botClient.AnswerCallbackQuery(callback.Id);
        }

        static Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            Console.WriteLine(exception.Message);
            return Task.CompletedTask;
        }

        static string CalculateSettlement(Dictionary<string, decimal> expenses)
        {
            var sb = new StringBuilder();
            if (expenses.Count == 0)
            {
                sb.AppendLine("هیچ داده‌ای وارد نشده است ❌");
                return sb.ToString();
            }

            decimal totalExpense = expenses.Values.Sum();
            int count = expenses.Count;
            decimal avgExpense = totalExpense / count;

            // بدهکارها و بستانکارها
            var balances = expenses.ToDictionary(kvp => kvp.Key, kvp => kvp.Value - avgExpense);
            var debtors = new Queue<KeyValuePair<string, decimal>>(balances.Where(x => x.Value < 0));
            var creditors = new Queue<KeyValuePair<string, decimal>>(balances.Where(x => x.Value > 0));

            while (debtors.Any() && creditors.Any())
            {
                var debtor = debtors.Dequeue();
                var creditor = creditors.Dequeue();

                decimal debt = Math.Min(-debtor.Value, creditor.Value);
                sb.AppendLine($"{debtor.Key} باید {debt:N0} به {creditor.Key} بدهد.");

                decimal newDebt = debtor.Value + debt;
                decimal newCredit = creditor.Value - debt;

                if (newDebt < 0) debtors.Enqueue(new KeyValuePair<string, decimal>(debtor.Key, newDebt));
                if (newCredit > 0) creditors.Enqueue(new KeyValuePair<string, decimal>(creditor.Key, newCredit));
            }

            if (sb.Length == 0)
                sb.AppendLine("همه تسویه هستند ✅");

            return sb.ToString();
        }
    }
}
