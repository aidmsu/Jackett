﻿using CsQuery;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Jackett.Indexers
{
    public class ThePirateBay : IndexerInterface
    {

        class ThePirateBayConfig : ConfigurationData
        {
            public StringItem Url { get; private set; }

            public ThePirateBayConfig()
            {
                Url = new StringItem { Name = "Url", ItemType = ItemType.InputString, Value = "https://thepiratebay.se/" };
            }

            public override Item[] GetItems()
            {
                return new Item[] { Url };
            }
        }

        public event Action<IndexerInterface, Newtonsoft.Json.Linq.JToken> OnSaveConfigurationRequested;

        public string DisplayName { get { return "The Pirate Bay"; } }

        public string DisplayDescription { get { return "The worlds largest bittorrent indexer"; } }

        public Uri SiteLink { get { return new Uri("https://thepiratebay.se/"); } }

        public bool IsConfigured { get; private set; }

        static string SearchUrl = "s/?q=\"{0}\"&category=205&page=0&orderby=99";
        static string BrowserUrl = "browse/200";
        static string SwitchSingleViewUrl = "switchview.php?view=s";

        string BaseUrl;

        CookieContainer cookies;
        HttpClientHandler handler;
        HttpClient client;


        public ThePirateBay()
        {
            IsConfigured = false;
            cookies = new CookieContainer();
            handler = new HttpClientHandler
            {
                CookieContainer = cookies,
                AllowAutoRedirect = true,
                UseCookies = true,
            };
            client = new HttpClient(handler);
        }

        public Task<ConfigurationData> GetConfigurationForSetup()
        {
            var config = new ThePirateBayConfig();
            return Task.FromResult<ConfigurationData>(config);
        }

        public async Task ApplyConfiguration(JToken configJson)
        {
            var config = new ThePirateBayConfig();
            config.LoadValuesFromJson(configJson);
            await TestBrowse(config.Url.Value);
            BaseUrl = new Uri(config.Url.Value).ToString();

            var message = new HttpRequestMessage
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(BaseUrl + SwitchSingleViewUrl)
            };
            message.Headers.Referrer = new Uri(BaseUrl + BrowserUrl);
            var response = await client.SendAsync(message);

            var configSaveData = new JObject();
            configSaveData["base_url"] = BaseUrl;

            if (OnSaveConfigurationRequested != null)
                OnSaveConfigurationRequested(this, configSaveData);

            IsConfigured = true;
        }

        public async Task VerifyConnection()
        {
            await TestBrowse(BaseUrl);
        }


        async Task TestBrowse(string url)
        {
            var result = await client.GetStringAsync(new Uri(url) + BrowserUrl);
            if (!result.Contains("<table id=\"searchResult\">"))
            {
                throw new Exception("Could not detect The Pirate Bay content");
            }
        }

        public void LoadFromSavedConfiguration(JToken jsonConfig)
        {
            BaseUrl = (string)jsonConfig["base_url"];
            IsConfigured = true;
        }

        public async Task<ReleaseInfo[]> PerformQuery(TorznabQuery query)
        {
            List<ReleaseInfo> releases = new List<ReleaseInfo>();

            var searchUrl = BaseUrl + string.Format(SearchUrl, HttpUtility.UrlEncode("game of thrones s05e01"));

            var message = new HttpRequestMessage
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(BaseUrl + SwitchSingleViewUrl)
            };
            message.Headers.Referrer = new Uri(searchUrl);

            var response = await client.SendAsync(message);
            var results = await response.Content.ReadAsStringAsync();

            CQ dom = results;

            var rows = dom["#searchResult > tbody > tr"];
            foreach (var row in rows)
            {
                var release = new ReleaseInfo();
                CQ qRow = row.Cq();
                CQ qLink = row.ChildElements.ElementAt(1).Cq().Children("a").First();

                release.MinimumRatio = 1;
                release.MinimumSeedTime = 172800;
                release.Title = qLink.Text().Trim();
                release.Description = release.Title;
                release.Comments = new Uri(BaseUrl + qLink.Attr("href").TrimStart('/'));
                release.Guid = release.Comments;

                var timeString = row.ChildElements.ElementAt(2).Cq().Text();
                if (timeString.Contains("mins ago"))
                    release.PublishDate = (DateTime.Now - TimeSpan.FromMinutes(int.Parse(timeString.Split(' ')[0])));
                else if (timeString.Contains("Today"))
                    release.PublishDate = (DateTime.UtcNow - TimeSpan.FromHours(2) - TimeSpan.Parse(timeString.Split(' ')[1])).ToLocalTime();
                else if (timeString.Contains("Y-day"))
                    release.PublishDate = (DateTime.UtcNow - TimeSpan.FromHours(26) - TimeSpan.Parse(timeString.Split(' ')[1])).ToLocalTime();
                else if (timeString.Contains(':'))
                {
                    var utc = DateTime.ParseExact(timeString, "MM-dd HH:mm", CultureInfo.InvariantCulture) - TimeSpan.FromHours(2);
                    release.PublishDate = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
                }
                else
                {
                    var utc = DateTime.ParseExact(timeString, "MM-dd yyyy", CultureInfo.InvariantCulture) - TimeSpan.FromHours(2);
                    release.PublishDate = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
                }

                var downloadCol = row.ChildElements.ElementAt(3).Cq().Find("a");
                release.MagnetUrl = new Uri(downloadCol.Attr("href"));
                release.InfoHash = release.MagnetUrl.ToString().Split(':')[3].Split('&')[0];

                var sizeString = row.ChildElements.ElementAt(4).Cq().Text().Split(' ');
                var sizeVal = float.Parse(sizeString[0]);
                var sizeUnit = sizeString[1];
                switch (sizeUnit)
                {
                    case "GiB": release.Size = ReleaseInfo.BytesFromGB(sizeVal); break;
                    case "MiB": release.Size = ReleaseInfo.BytesFromMB(sizeVal); break;
                    case "KiB": release.Size = ReleaseInfo.BytesFromKB(sizeVal); break;
                }

                release.Seeders = int.Parse(row.ChildElements.ElementAt(5).Cq().Text());
                release.Peers = int.Parse(row.ChildElements.ElementAt(6).Cq().Text()) + release.Seeders;

                releases.Add(release);
            }

            return releases.ToArray();

        }


        public Task<byte[]> Download(Uri link)
        {
            throw new NotImplementedException();
        }
    }
}
