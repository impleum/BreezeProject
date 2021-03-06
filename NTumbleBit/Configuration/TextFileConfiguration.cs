﻿using NBitcoin;
using NTumbleBit.ClassicTumbler;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace NTumbleBit.Configuration
{

	public class ConfigurationException : Exception
	{
		public ConfigurationException(string message) : base(message)
		{

		}
	}

	public class TextFileConfiguration
	{
		private Dictionary<string, List<string>> _Args;

		public TextFileConfiguration(string[] args)
		{
			_Args = new Dictionary<string, List<string>>();
			string noValueParam = null;
			Action flushNoValueParam = () =>
			{
				if(noValueParam != null)
				{
					Add(noValueParam, "1", false);
					noValueParam = null;
				}
			};

			foreach(var arg in args)
			{
				bool isParamName = arg.StartsWith("-", StringComparison.Ordinal);
				if(isParamName)
				{
					var splitted = arg.Split('=');
					if(splitted.Length > 1)
					{
						var value = String.Join("=", splitted.Skip(1).ToArray());
						flushNoValueParam();
						Add(splitted[0], value, false);
					}
					else
					{
						flushNoValueParam();
						noValueParam = splitted[0];
					}
				}
				else
				{
					if(noValueParam != null)
					{
						Add(noValueParam, arg, false);
						noValueParam = null;
					}
				}
			}
			flushNoValueParam();
		}

		private void Add(string key, string value, bool sourcePriority)
		{
			key = NormalizeKey(key);
            if (!_Args.TryGetValue(key, out List<string> list))
            {
                list = new List<string>();
                _Args.Add(key, list);
            }
            if (sourcePriority)
				list.Insert(0, value);
			else
				list.Add(value);
		}

		private static string NormalizeKey(string key)
		{
			key = key.ToLowerInvariant();
			while(key.Length > 0 && key[0] == '-')
			{
				key = key.Substring(1);
			}
			key = key.Replace(".", "");
			return key;
		}

		public void MergeInto(TextFileConfiguration destination, bool sourcePriority)
		{
			foreach(var kv in _Args)
			{
				foreach(var v in kv.Value)
					destination.Add(kv.Key, v, sourcePriority);
			}
		}

		public TextFileConfiguration(Dictionary<string, List<string>> args)
		{
			_Args = args;
		}

		public static TextFileConfiguration Parse(string data)
		{
			Dictionary<string, List<string>> result = new Dictionary<string, List<string>>();
			var lines = data.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
			int lineCount = -1;
			foreach(var l in lines)
			{
				lineCount++;
				var line = l.Trim();
				if(line.StartsWith("#", StringComparison.Ordinal))
					continue;
				var split = line.Split('=');
				if(split.Length == 0)
					continue;
				if(split.Length == 1)
					throw new ConfigException("Line " + lineCount + ": No value are set");

				var key = split[0];
				key = NormalizeKey(key);
                if (!result.TryGetValue(key, out List<string> values))
                {
                    values = new List<string>();
                    result.Add(key, values);
                }
                var value = String.Join("=", split.Skip(1).ToArray());
				values.Add(value);
			}
			return new TextFileConfiguration(result);
		}

		public bool Contains(string key)
		{
            return _Args.TryGetValue(key, out List<string> values);
        }
		public string[] GetAll(string key)
		{
            if (!_Args.TryGetValue(key, out List<string> values))
                return new string[0];
            return values.ToArray();
		}

		private List<Tuple<string, string>> _Aliases = new List<Tuple<string, string>>();

		public void AddAlias(string from, string to)
		{
			from = NormalizeKey(from);
			to = NormalizeKey(to);
			_Aliases.Add(Tuple.Create(from, to));
		}
		public T GetOrDefault<T>(string key, T defaultValue)
		{
			var notNormalizedKey = key;
			key = NormalizeKey(key);

			var aliases = _Aliases
				.Where(a => a.Item1 == key || a.Item2 == key)
				.Select(a => a.Item1 == key ? a.Item2 : a.Item1)
				.ToList();
			aliases.Insert(0, key);

			foreach(var alias in aliases)
			{
                if (!_Args.TryGetValue(alias, out List<string> values))
                    continue;
                if (values.Count == 0)
					continue;
				try
				{
					return ConvertValue<T>(values[0]);
				}
				catch(FormatException) { throw new ConfigException("Key " + notNormalizedKey + " should be of type " + typeof(T).Name); }
				catch(ConfigException ex)
				{
					throw new ConfigException(notNormalizedKey + " : " +  ex.Message);
				}
			}
			return defaultValue;
		}

		private T ConvertValue<T>(string str)
		{
			if(typeof(T) == typeof(bool))
			{
				var trueValues = new[] { "1", "true" };
				var falseValues = new[] { "0", "false" };
				if(trueValues.Contains(str, StringComparer.OrdinalIgnoreCase))
					return (T)(object)true;
				if(falseValues.Contains(str, StringComparer.OrdinalIgnoreCase))
					return (T)(object)false;
				throw new FormatException();
			}
			else if(typeof(T) == typeof(Uri))
				return (T)(object)new Uri(str, UriKind.Absolute);
			else if(typeof(T) == typeof(string))
				return (T)(object)str;
			else if(typeof(T) == typeof(Money))
				return (T)(object)Money.Parse(str);
			else if(typeof(T) == typeof(TumblerUrlBuilder))
				try
				{

					return (T)(object)new TumblerUrlBuilder(str);
				}
				catch { throw new ConfigException("Invalid tumbler uri"); }
			else if(typeof(T) == typeof(IPEndPoint))
			{
				var separator = str.LastIndexOf(":");
				if(separator == -1)
					throw new FormatException();
				var ip = str.Substring(0, separator);
				var port = str.Substring(separator + 1);
				return (T)(object)new IPEndPoint(IPAddress.Parse(ip), int.Parse(port));
			}
			else if(typeof(T) == typeof(int))
			{
				return (T)(object)int.Parse(str, CultureInfo.InvariantCulture);
			}
			else if (typeof(T).IsEnum)
			{
				return (T)Enum.Parse(typeof(T), str, true);
			}
			else
			{
				throw new NotSupportedException("Configuration value does not support time " + typeof(T).Name);
			}
		}

		//public static String CreateDefaultConfiguration(Network network)
		//{
		//	StringBuilder builder = new StringBuilder();
		//	builder.AppendLine("#rpc.url=http://localhost:" + network.RPCPort + "/");
		//	builder.AppendLine("#rpc.user=bitcoinuser");
		//	builder.AppendLine("#rpc.password=bitcoinpassword");
		//	builder.AppendLine("#rpc.cookiefile=yourbitcoinfolder/.cookie");
		//	return builder.ToString();
		//}

		//public static String CreateClientDefaultConfiguration(Network network)
		//{
		//	StringBuilder builder = new StringBuilder();
		//	builder.AppendLine("#rpc.url=http://localhost:" + network.RPCPort + "/");
		//	builder.AppendLine("#rpc.user=bitcoinuser");
		//	builder.AppendLine("#rpc.password=bitcoinpassword");
		//	builder.AppendLine("#rpc.cookiefile=yourbitcoinfolder/.cookie");
		//	return builder.ToString();
		//}
	}
}

