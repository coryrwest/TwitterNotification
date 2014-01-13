using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace TwitterNotification {
	class Program {
		static void Main(string[] args) {
			// Set constants
			int HOUR_RANGE = 24;
			string URL = "";
			string TO_EMAIL = "";
			string FROM_EMAIL = "";
			string SMTP_PASSWORD = "";
			string EMAIL_SUBJECT = "Recent Tweets";
			string SMTP_HOST = "";
			int SMTP_PORT = 587;

			// If no settings.config
			if (!File.Exists(AppDomain.CurrentDomain.SetupInformation.ConfigurationFile)) {
				Console.WriteLine("No exe.config, please include the exe.config file in the directory you are running this app from.");
				Console.ReadKey();
				Environment.Exit(0);
			}

			// Get Settings
			URL = GetConfigItem("URL");
			if (URL == null || !URL.Contains("twitter")) {
				Console.WriteLine("Not a valid twitter url");
				Console.ReadKey();
				Environment.Exit(0);
			}

			string temp = GetConfigItem("hours", "hours must be a positive number.");
			HOUR_RANGE = Convert.ToInt32(temp);

			TO_EMAIL = GetConfigItem("toEmail");
			FROM_EMAIL = GetConfigItem("fromEmail");
			SMTP_PASSWORD = GetConfigItem("smtpPassword");
			SMTP_HOST = GetConfigItem("smtpHost");
			temp = GetConfigItem("smtpPort");
			SMTP_PORT = Convert.ToInt32(temp);
			EMAIL_SUBJECT = GetConfigItem("emailSubject", "Please use an email subject on the configuration.");

			// Get source
			string source = "";
			try {
				source = GetSourceOfURL(URL);
			}
			catch (Exception e) {
				Console.WriteLine("The URL you entered is not valid");
				Console.WriteLine(e.Message);
				Console.ReadKey();
				Environment.Exit(0);
			}

			// Strip source to just tweets
			source = TrimStringToBlock("stream-items-id", "stream-footer", source);
			if (string.IsNullOrEmpty(source)) {
				Console.WriteLine("Something was wrong with the source. Is Twitter down? Is your url wrong?");
				Console.ReadKey();
				Environment.Exit(0);
			}

			// Find all tweets and add to collection
			List<string> tweets = new List<string>();
			tweets = FindAllStringMatches(source, "icon dogear", "stream-item-footer");

			// Further refine tweets
			List<string> tweetsText = new List<string>();
			tweetsText = FindAllStringMatches(source, "tweet-text\">", "</p", false);

			// Get dates
			List<DateTime> tweetsDate = new List<DateTime>();
			foreach (string tweet in tweets) {
				DateTime dt = ParseToDate.ParseDate(tweet);
				tweetsDate.Add(dt);
			}

			List<Tweet> tweetsDecode = new List<Tweet>();
			for (int i = 0; i < tweetsText.Count; i++) {
				Tweet tweet = new Tweet();
				tweet.Text = WebUtility.HtmlDecode(tweetsText[i]);
				tweet.Text = tweet.Text.Replace("\"", "\'");
				tweet.Date = tweetsDate[i];
				tweetsDecode.Add(tweet);
			}

			// Sort 24 hour tweets
			List<Tweet> tweets24Hr = new List<Tweet>();
			foreach (Tweet tweet in tweetsDecode) {
				if (tweet.Date > DateTime.Now.AddHours(-HOUR_RANGE)) {
					tweets24Hr.Add(tweet);
				}
			}
			tweets24Hr = tweets24Hr.OrderByDescending(o => o.Date).ToList();

			// Email all tweets in last 24 hours
			StringBuilder builder = new StringBuilder();
			
			foreach (Tweet tweet in tweets24Hr) {
				builder = builder.AppendFormat("{0}", tweet.Date);
				builder = builder.Append("<br>");
				builder = builder.AppendFormat("{0}", tweet.Text);
				builder = builder.Append("<hr>");
			}

			var smtp = new SmtpClient
			{
				Host = SMTP_HOST,
				Port = SMTP_PORT,
				EnableSsl = true,
				DeliveryMethod = SmtpDeliveryMethod.Network,
				UseDefaultCredentials = false,
				Credentials = new NetworkCredential(FROM_EMAIL, SMTP_PASSWORD)
			};

			MailMessage message = new MailMessage();
			message.To.Add(new MailAddress(TO_EMAIL));
			message.Subject = EMAIL_SUBJECT + " - " + DateTime.Now.ToShortDateString();
			message.From = new MailAddress(FROM_EMAIL);
			message.Body = builder.ToString();
			message.IsBodyHtml = true;

			try {
			smtp.Send(message);
			}
			catch (Exception) {
				Console.WriteLine("Something was wrong with either your to address or your smtp settings.");
				Console.ReadKey();
				Environment.Exit(0);
			}
		}

		public class Tweet {
			public string Text { get; set; }
			public DateTime Date { get; set; }
		}
		
		#region Helpers
		public static class ParseToDate {
			public static DateTime ParseDate(string date) {
				date = FindStringMatch(WebUtility.HtmlDecode(date), "data-time=\"", "\"", false);
				DateTime d = new DateTime();
				d = UnixTimeStampToDateTime(Convert.ToDouble(date));
				return d;
			}
		}

	        public static DateTime UnixTimeStampToDateTime(double unixTimeStamp) {
	            // Unix timestamp is seconds past epoch
	            DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0);
	            dtDateTime = dtDateTime.AddSeconds(unixTimeStamp).ToLocalTime();
	            return dtDateTime;
	        }
	        
		public static string GetConfigItem(string configItem, string error = "is empty.") {
			try {
				return ConfigurationManager.AppSettings[configItem];
			}
			catch (Exception) {
				if (error == "is empty.")
					Console.WriteLine(configItem + " " + error);
				else
					Console.WriteLine(error);
				Console.ReadKey();
				Environment.Exit(0);
			}
			return null;
		}

		/// <summary>
		/// Will return the index of the next match after the supplied index
		/// </summary>
		/// <param name="text">Text to match on</param>
		/// <param name="startIndex">index to start search at</param>
		/// <param name="match">what to match on</param>
		/// <returns>int of next match after startIndex</returns>
		public static int IndexAfter(string text, int startIndex, string match) {
			if (startIndex == -1) {
				return -1;
			}
			// Cut beginning of text to the startIndex
			text = text.Substring(startIndex);
			// Check for a match, if not exit
			if (text.IndexOf(match) == -1) {
				return -1;
			}
			// Get index of the first match after the startIndex
			int index = text.IndexOf(match) + startIndex;
			return index;
		}

		/// <summary>
		/// Wil trim a string to the block of text between the two matches.
		/// Including the text of the match itself.
		/// </summary>
		/// <param name="startMatch"></param>
		/// <param name="endMatch"></param>
		/// <param name="text"></param>
		/// <returns></returns>
		public static string TrimStringToBlock(string startMatch, string endMatch, string text, bool includeMatch = true) {
			int startIndex = 0;
			int endIndex = 0;
			if (includeMatch) {
				startIndex = text.IndexOf(startMatch);
				endIndex = IndexAfter(text, startIndex, endMatch);
			}
			else {
				startIndex = text.IndexOf(startMatch) + startMatch.Length;
				endIndex = IndexAfter(text, startIndex, endMatch) - endMatch.Length;
			}
			// Check for existing endIndex
			if (endIndex == -1) {
				return null;
			}
			// Get substring of text block plus the length of the match
			text = text.Substring(startIndex, endIndex - startIndex + endMatch.Length);
			return text;
		}

		/// <summary>
		/// Finds a matche in the string provided and returns it. Optionally include the match itself.
		/// </summary>
		/// <param name="text"></param>
		/// <param name="startMatch"></param>
		/// <param name="endMatch"></param>
		/// <param name="includeMatch">include the match text in the returned value.</param>
		/// <returns></returns>
		public static string FindStringMatch(string text, string startMatch, string endMatch = "", bool includeMatch = true) {
			string tempText = "";
			string results = "";

			if (string.IsNullOrEmpty(endMatch)) {
				tempText = TrimStringToBlock(startMatch, startMatch, text);
				int startIndex = text.IndexOf(startMatch) + tempText.Length;
				results = text.Substring(startIndex);
			}
			else {
				string replaceText = TrimStringToBlock(startMatch, endMatch, text);
				if (includeMatch) {
					tempText = TrimStringToBlock(startMatch, endMatch, text);
				}
				else {
					tempText = TrimStringToBlock(startMatch, endMatch, text, false);
				}
				text = text.Replace(replaceText, "");
			}
			results = tempText;

			return results;
		}

		/// <summary>
		/// Finds all matches in the string provided and returns a list. Optionally include the match itself.
		/// </summary>
		/// <param name="text"></param>
		/// <param name="startMatch"></param>
		/// <param name="endMatch"></param>
		/// <param name="includeMatch">include the match text in the returned value.</param>
		/// <returns></returns>
		public static List<string> FindAllStringMatches(string text, string startMatch, string endMatch = "", bool includeMatch = true) {
			string tempText = "";
			List<string> results = new List<string>();

			while (text.IndexOf(startMatch) != -1) {
				if (string.IsNullOrEmpty(endMatch)) {
					tempText = TrimStringToBlock(startMatch, startMatch, text);
					int startIndex = text.IndexOf(startMatch) + tempText.Length;
					text = text.Substring(startIndex);
				}
				else {
					string replaceText = TrimStringToBlock(startMatch, endMatch, text);
					if (includeMatch) {
						tempText = TrimStringToBlock(startMatch, endMatch, text);
					}
					else {
						tempText = TrimStringToBlock(startMatch, endMatch, text, false);
					}
					text = text.Replace(replaceText, "");
				}
				results.Add(tempText);
			}
			return results;
		}

		/// <summary>
		/// Returns the HTML source of the given URL as a string
		/// </summary>
		/// <param name="Url"></param>
		/// <returns></returns>
		public static string GetSourceOfURL(string Url) {
			HttpWebRequest myRequest = (HttpWebRequest)WebRequest.Create(Url);
			myRequest.Method = "GET";
			WebResponse myResponse = myRequest.GetResponse();
			StreamReader sr = new StreamReader(myResponse.GetResponseStream(), System.Text.Encoding.UTF8);
			string result = sr.ReadToEnd();
			sr.Close();
			myResponse.Close();

			return result;
		}
		#endregion
	}
}
