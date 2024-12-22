using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Nethereum.Web3;
using Nethereum.Contracts;
using Newtonsoft.Json;
using RestSharp;
using Telegram.Bot;

namespace WalletBalanceChecker
{
    internal class Program
    {
        private static readonly string RpcUrl = "https://bsc-dataseed.binance.org/"; // RPC-адрес узла BSC
        private static readonly string ContractAddress = "0xf486ad071f3bEE968384D2E39e2D8aF0fCf6fd46"; // Адрес контракта
        private static readonly string ApiKey = "HHR6XP1G5JWE7ACR8X2B8GF1ZD6SNZVZNU"; // API-ключ
        private static readonly string TelegramBotToken = "7994007891:AAGpWidV5nMzpIPBhNEfx-xaR0cY1qwQRtc"; // Токен Telegram бота
        private static readonly string TelegramChatId = "-1002322975978"; // ID вашего чата

        private static readonly List<string> Addresses = new()
{


    "0x7dd617eacd7Fd35f69275f943Ff82218213796b7",
    "0x5e10B2247a430f97c94205894B9185F687A32345",
    "0x13c5C83cf9B9aC68FA18272B756Bce1635196132",
    "0x022af5ce19720a938Ba8C9E66FdF1Da1606298eF",
    "0x37cCcC19acAB91E8bC6074Cb4EaaFef1185ee1Bb",
    "0x051bB49EdB865Bb4cC9277BbB132C922403B07e4",
    "0x2703E5D3709782e85957E40a9c834AFD4D45caF9",
    "0x5935DC3250a0d8a0aC7c2e4AB925C4FEf2F8FDf8",
    "0x59098E3c6C5Bcbecb4117C6eF59b341d1F0F3083",
    "0xDa000FA80C5E9cb4E24a66bFF6a56cC454422e78",
    "0xc12A93bf62CfD50620BCfDDD903913903DF647B4",
    "0xc322a2110958c1365e88D88aef65Ebdf335b6E67"
};


        private static readonly Dictionary<string, decimal> LastBalances = new();
        private static readonly Dictionary<string, BigInteger> LastTotalStaked = new();

        static async Task Main(string[] args)
        {
            Console.WriteLine("Запуск программы мониторинга балансов и общего TVL...");

            foreach (var address in Addresses)
                LastBalances[address] = 0; // Инициализация пустыми значениями

            while (true)
            {
                await CheckBalancesAndNotifyAsync();
                await CheckTotalStakedAsync();
                //await CheckTokenPriceAndNotifyAsync(); // Добавляем проверку цены токена VELO
                Console.WriteLine($"Проверка завершена. Следующая проверка через 10 минут...");
                Thread.Sleep(TimeSpan.FromMinutes(10)); // Задержка 10 минут
            }
        }

        private static async Task CheckBalancesAndNotifyAsync()
        {
            var client = new RestClient("https://api.bscscan.com/api");

            foreach (var address in Addresses)
            {
                Console.WriteLine($"Проверяем баланс для адреса: {address}");
                var request = new RestRequest($"?module=account&action=tokenbalance&contractaddress={ContractAddress}&address={address}&tag=latest&apikey={ApiKey}", Method.Get);

                var response = await client.ExecuteAsync(request);
                if (!response.IsSuccessful)
                {
                    Console.WriteLine($"Ошибка при запросе баланса: {response.ErrorMessage}");
                    continue;
                }

                try
                {
                    var apiResponse = JsonConvert.DeserializeObject<ApiResponse>(response.Content);
                    if (apiResponse?.Result != null)
                    {
                        BigInteger balanceBigInt = BigInteger.Parse(apiResponse.Result);
                        decimal balance = (decimal)balanceBigInt / (decimal)Math.Pow(10, 18);

                        Console.WriteLine($"Баланс токенов: {balance:N8}");

                        if (LastBalances[address] != balance)
                        {
                            // Если баланс изменился, отправляем уведомление
                            string changeType = balance > LastBalances[address] ? "пополнение" : "снятие";
                            decimal difference = Math.Abs(balance - LastBalances[address]);

                            await SendTelegramMessageAsync($"Изменение баланса на адресе {address}:\nТип: {changeType}\nИзменение: {difference:N8} токенов\nТекущий баланс: {balance:N8}");

                            // Обновляем последний баланс
                            LastBalances[address] = balance;
                        }
                    }
                    else
                    {
                        Console.WriteLine("Ошибка: Невозможно получить баланс из ответа API.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка обработки данных для адреса {address}: {ex.Message}");
                }
            }
        }

        private static async Task CheckTotalStakedAsync()
        {
            var web3 = new Web3(RpcUrl);
            string abi = @"[ { ""constant"": true, ""inputs"": [], ""name"": ""totalStaked"", ""outputs"": [ { ""name"": """", ""type"": ""uint256"" } ], ""payable"": false, ""stateMutability"": ""view"", ""type"": ""function"" } ]";
            string methodName = "totalStaked";
            BigInteger totalLockedValue = BigInteger.Zero;

            foreach (var address in Addresses)
            {
                try
                {
                    var contract = web3.Eth.GetContract(abi, address);
                    var totalStakedFunction = contract.GetFunction(methodName);
                    var result = await totalStakedFunction.CallAsync<BigInteger>();
                    totalLockedValue += result;
                    Console.WriteLine($"Address: {address}, Total Staked: {Web3.Convert.FromWei(result)}");

                    // Проверка на значительное изменение TVL
                    if (LastTotalStaked.ContainsKey(address))
                    {
                        decimal newTotalStaked = Web3.Convert.FromWei(result);
                        decimal lastTotalStaked = Web3.Convert.FromWei(LastTotalStaked[address]);

                        decimal percentageChange = (newTotalStaked - lastTotalStaked) / lastTotalStaked * 100;
                        if (Math.Abs(percentageChange) > 10)
                        {
                            // Отправка сообщения в Telegram
                            await SendTelegramMessageAsync($"Значительное изменение TVL на {percentageChange:N2}% для адреса {address} — возможно, пора проверять цену.");
                        }
                    }

                    // Обновляем значение TVL для адреса
                    LastTotalStaked[address] = result;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при обработке адреса {address} для TVL: {ex.Message}");
                }
            }

            // Отправка общего TVL в Telegram
            string totalLockedValueMessage = $"Общий TVL: {Web3.Convert.FromWei(totalLockedValue)}";
            await SendTelegramMessageAsync(totalLockedValueMessage);  // Отправляем сообщение в Telegram
        }

        //private static async Task<decimal> GetTokenPriceAsync(string tokenSymbol)
        //{
        //    var client = new RestClient($"https://api.coingecko.com/api/v3/simple/price?ids={tokenSymbol}&vs_currencies=usd");
        //    var request = new RestRequest($"?ids={tokenSymbol}&vs_currencies=usd", Method.Get); // Метод передаем как строку

        //    var response = await client.ExecuteAsync(request);

        //    if (response.IsSuccessful)
        //    {
        //        var priceData = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, decimal>>>(response.Content);
        //        return priceData[tokenSymbol]["usd"];
        //    }
        //    return 0;
        //}
        //private static async Task CheckTokenPriceAndNotifyAsync()
        //{
        //    decimal currentPrice = await GetTokenPriceAsync("velodrome"); // Получаем цену токена VELO с CoinGecko
        //    decimal priceThreshold = 5.0m; // Например, пороговое значение для продажи

        //    Console.WriteLine($"Текущая цена токена VELO: {currentPrice}");

        //    if (currentPrice > priceThreshold)
        //    {
        //        await SendTelegramMessageAsync("Цена токенов VELO высокая, можно рассмотреть продажу.");
        //    }
        //    else
        //    {
        //        await SendTelegramMessageAsync("Цена токенов VELO низкая, можно рассмотреть покупку.");
        //    }
        //}




        private static async Task SendTelegramMessageAsync(string message)
        {
            using (var client = new HttpClient())
            {
                var url = $"https://api.telegram.org/bot{TelegramBotToken}/sendMessage";
                var parameters = new Dictionary<string, string>
        {
            { "chat_id", TelegramChatId },
            { "text", message }
        };
                var content = new FormUrlEncodedContent(parameters);

                var response = await client.PostAsync(url, content);
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine("Сообщение успешно отправлено в Telegram");
                }
                else
                {
                    Console.WriteLine($"Ошибка при отправке сообщения в Telegram: {response.StatusCode}");
                }
            }
        }


        // Класс для десериализации ответа API
        public class ApiResponse
        {
            [JsonProperty("status")]
            public string Status { get; set; }
            [JsonProperty("message")]
            public string Message { get; set; }
            [JsonProperty("result")]
            public string Result { get; set; }
        }
    }
}
