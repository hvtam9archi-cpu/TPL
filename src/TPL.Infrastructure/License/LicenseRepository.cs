using System;
using System.Collections.Generic;
using System.Management;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using TPL.Core.Helpers;
using TPL.Core.Logging;
using TPL.Domain.Interfaces;
using TPL.Domain.Models;

namespace TPL.Infrastructure.License
{
	/// <summary>
	/// Quản lý bản quyền: đọc/ghi Registry, xác thực key, kiểm tra revoke.
	/// Trích xuất từ LicenseManager.cs.
	/// </summary>
	public class LicenseRepository : ILicenseRepository
	{
		private const string SecretKey = "TPL_V1_SECRET_KEY_2026_NEVER_SHARE_THIS_EVER!!";
		private const string RegistryPath = @"Software\TPL\Settings";

		public const string RevokeListUrl = "https://docs.google.com/spreadsheets/d/e/2PACX-1vQINX-Qrie3CV-wo3xMZU7gwkMKcbBORTTQryY8af60V3sxG7_Q1QspoQ3o7GmxJmVTH5Q5_vfOWUr8/pub?gid=198057700&single=true&output=csv";

		private static readonly HttpClient _httpClient = new()
		{
			Timeout = TimeSpan.FromSeconds(10)
		};

		public LicenseInfo GetLicenseInfo()
		{
			string hwId = GetHardwareId();
			string encryptedData = null;
			try
			{
				using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath);
				encryptedData = key?.GetValue("LicenseData") as string;
			}
			catch { }

			if (string.IsNullOrEmpty(encryptedData))
			{
				var info = new LicenseInfo
				{
					HardwareId = hwId,
					TrialStartDate = DateTime.Now,
					ExpirationDate = DateTime.Now.AddDays(30),
					LastRunDate = DateTime.Now,
					AppliedKeys = new List<string>()
				};
				SaveLicenseInfo(info);
				return info;
			}

			try
			{
				string decrypted = CryptoHelper.Decrypt(encryptedData, SecretKey);
				var info = LicenseInfo.Deserialize(decrypted);
				if (info.HardwareId != hwId) info.IsHardwareChanged = true;
				return info;
			}
			catch
			{
				return new LicenseInfo { HardwareId = hwId, ExpirationDate = DateTime.MinValue, LastRunDate = DateTime.Now, IsHardwareChanged = true };
			}
		}

		public void SaveLicenseInfo(LicenseInfo info)
		{
			try
			{
				string serialized = info.Serialize();
				string encrypted = CryptoHelper.Encrypt(serialized, SecretKey);
				using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath);
				key?.SetValue("LicenseData", encrypted);
			}
			catch (Exception ex)
			{
				TplLogger.Error(ex, "SaveLicenseInfo");
			}
		}

		public void UpdateLastRunDate(LicenseInfo info)
		{
			if (info.IsValid && DateTime.Now >= info.LastRunDate)
			{
				info.LastRunDate = DateTime.Now;
				SaveLicenseInfo(info);
			}
		}

		public bool ActivateLicense(string activationKey, out string message)
		{
			try
			{
				string cleanedKey = activationKey.Replace("-", "").Replace(" ", "").ToUpper();
				if (cleanedKey.Length != 16)
				{
					message = "Invalid key length (expected 16 characters).";
					return false;
				}

				byte[] payload;
				try
				{
					payload = Base32.Decode(cleanedKey);
				}
				catch
				{
					message = "Invalid key characters.";
					return false;
				}

				if (payload.Length != 10)
				{
					message = "Invalid key format.";
					return false;
				}

				int days = (payload[0] << 8) | payload[1];

				byte[] shortHwIdBytes = new byte[4];
				Array.Copy(payload, 2, shortHwIdBytes, 0, 4);
				StringBuilder sbHw = new();
				foreach (byte b in shortHwIdBytes) sbHw.Append(b.ToString("X2"));
				string shortHwIdHex = sbHw.ToString();

				byte seqByte = payload[6];

				byte[] providedSigBytes = new byte[3];
				Array.Copy(payload, 7, providedSigBytes, 0, 3);

				string myHwId = GetHardwareId();
				string myShortHwIdHex = myHwId.Substring(0, Math.Min(8, myHwId.Length)).ToUpper();

				if (shortHwIdHex != myShortHwIdHex)
				{
					message = "Key does not match this hardware.";
					return false;
				}

				string textToHash = $"{shortHwIdHex}|{days}|{seqByte}|{SecretKey}";
				byte[] hashBytes = CryptoHelper.GetHashSha256(textToHash);

				for (int i = 0; i < 3; i++)
				{
					if (providedSigBytes[i] != hashBytes[i])
					{
						message = "Key signature is invalid (tampered).";
						return false;
					}
				}

				LicenseInfo info = GetLicenseInfo();

				if (info.AppliedKeys.Contains(cleanedKey))
				{
					message = "This key has already been applied.";
					return false;
				}

				if (info.IsHardwareChanged)
				{
					info.HardwareId = GetHardwareId();
					info.IsHardwareChanged = false;
				}

				DateTime baseDate = DateTime.Now > info.ExpirationDate ? DateTime.Now : info.ExpirationDate;

				if (days >= 9999 || baseDate == DateTime.MaxValue)
				{
					info.ExpirationDate = DateTime.MaxValue;
				}
				else
				{
					try { info.ExpirationDate = baseDate.AddDays(days); }
					catch (ArgumentOutOfRangeException) { info.ExpirationDate = DateTime.MaxValue; }
				}

				info.AppliedKeys.Add(cleanedKey);
				info.LastRunDate = DateTime.Now;
				SaveLicenseInfo(info);

				string expiryDisplay = info.ExpirationDate == DateTime.MaxValue
					? "Permanent"
					: info.ExpirationDate.ToString("dd/MM/yyyy HH:mm");
				message = $"License activated! Expires: {expiryDisplay}";
				return true;
			}
			catch (Exception ex)
			{
				message = $"Activation error: {ex.Message}";
				return false;
			}
		}

		public string GetHardwareId()
		{
			string cpuId = GetWmiProperty("Win32_Processor", "ProcessorId");
			string boardId = GetWmiProperty("Win32_BaseBoard", "SerialNumber");
			return CryptoHelper.ComputeMD5(cpuId + boardId).ToUpper();
		}

		public void CheckRemoteRevokeAsync()
		{
			if (string.IsNullOrEmpty(RevokeListUrl) || !RevokeListUrl.StartsWith("http")) return;

			Task.Run(async () =>
			{
				try
				{
					string url = RevokeListUrl + (RevokeListUrl.Contains("?") ? "&" : "?") + "_t=" + DateTime.Now.Ticks;

					using var request = new HttpRequestMessage(HttpMethod.Get, url);
					request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };

					using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
					response.EnsureSuccessStatusCode();
					string data = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

					string hwId = GetHardwareId().ToUpper();
					string cleanData = data.Replace("-", "").Replace(" ", "").ToUpper();

					LicenseInfo info = GetLicenseInfo();

					bool isRevoked = cleanData.Contains(hwId);
					if (!isRevoked)
					{
						foreach (string key in info.AppliedKeys)
						{
							if (!string.IsNullOrEmpty(key) && cleanData.Contains(key.ToUpper()))
							{ isRevoked = true; break; }
						}
					}

					if (isRevoked && info.IsValid)
					{
						info.ExpirationDate = DateTime.MinValue;
						SaveLicenseInfo(info);
					}
				}
				catch (TaskCanceledException) { }
				catch { }
			});
		}

		public string GenerateKey(string hwId, int days, string seq = "")
		{
			string shortHwIdHex = hwId.Substring(0, Math.Min(8, hwId.Length)).ToUpper().PadRight(8, '0');
			byte[] shortHwIdBytes = new byte[4];
			for (int i = 0; i < 4; i++)
				shortHwIdBytes[i] = Convert.ToByte(shortHwIdHex.Substring(i * 2, 2), 16);

			byte[] daysBytes = new byte[2];
			daysBytes[0] = (byte)(days >> 8);
			daysBytes[1] = (byte)(days & 0xFF);

			byte seqByte = 0;
			if (!string.IsNullOrEmpty(seq))
			{
				int sum = 0;
				foreach (char c in seq) sum = (sum + (int)c) % 256;
				seqByte = (byte)sum;
			}

			string textToHash = $"{shortHwIdHex}|{days}|{seqByte}|{SecretKey}";
			byte[] hashBytes = CryptoHelper.GetHashSha256(textToHash);
			byte[] sigBytes = new byte[3];
			Array.Copy(hashBytes, 0, sigBytes, 0, 3);

			byte[] payload = new byte[10];
			Array.Copy(daysBytes, 0, payload, 0, 2);
			Array.Copy(shortHwIdBytes, 0, payload, 2, 4);
			payload[6] = seqByte;
			Array.Copy(sigBytes, 0, payload, 7, 3);

			string rawBase32 = Base32.Encode(payload);

			StringBuilder formatted = new();
			for (int i = 0; i < 16; i++)
			{
				if (i > 0 && i % 4 == 0) formatted.Append("-");
				formatted.Append(rawBase32[i]);
			}
			return formatted.ToString();
		}

		private static string GetWmiProperty(string wmiclass, string property)
		{
			try
			{
				using ManagementObjectSearcher searcher = new($"SELECT {property} FROM {wmiclass}");
				foreach (ManagementBaseObject obj in searcher.Get())
					return obj[property]?.ToString()?.Trim() ?? "";
			}
			catch { }
			return "UNKNOWN";
		}
	}
}
