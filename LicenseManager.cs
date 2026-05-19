using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace TPL
{
	public class LicenseInfo
	{
		public string HardwareId { get; set; }
		public DateTime TrialStartDate { get; set; }
		public DateTime ExpirationDate { get; set; }
		public DateTime LastRunDate { get; set; }
		public List<string> AppliedKeys { get; set; } = new List<string>();
		public bool IsHardwareChanged { get; set; } = false;

		public bool IsValid
		{
			get
			{
				if (IsHardwareChanged) return false;
				if (DateTime.Now < LastRunDate) return false; // Clock was turned back
				if (DateTime.Now > ExpirationDate && ExpirationDate != DateTime.MaxValue) return false;
				return true;
			}
		}

		public string Serialize()
		{
			return $"{HardwareId};{TrialStartDate.Ticks};{ExpirationDate.Ticks};{LastRunDate.Ticks};{string.Join(",", AppliedKeys)}";
		}

		public static LicenseInfo Deserialize(string data)
		{
			var parts = data.Split(';');
			var info = new LicenseInfo
			{
				HardwareId = parts[0],
				TrialStartDate = new DateTime(long.Parse(parts[1])),
				ExpirationDate = new DateTime(long.Parse(parts[2])),
				LastRunDate = new DateTime(long.Parse(parts[3]))
			};
			if (parts.Length > 4 && !string.IsNullOrEmpty(parts[4]))
			{
				info.AppliedKeys = new List<string>(parts[4].Split(','));
			}
			return info;
		}
	}

	public static class LicenseManager
	{
		private const string SecretKey = "TPL_V1_SECRET_KEY_2026_NEVER_SHARE_THIS_EVER!!"; // 256-bit+
		private const string RegistryPath = @"Software\TPL\Settings";

		// Thay link Google Sheets (định dạng CSV) của bạn vào đây. Xem hướng dẫn đi kèm.
		public const string RevokeListUrl = "https://docs.google.com/spreadsheets/d/e/2PACX-1vQINX-Qrie3CV-wo3xMZU7gwkMKcbBORTTQryY8af60V3sxG7_Q1QspoQ3o7GmxJmVTH5Q5_vfOWUr8/pub?gid=198057700&single=true&output=csv";

		public static void CheckRemoteRevokeAsync()
		{
			if (string.IsNullOrEmpty(RevokeListUrl) || !RevokeListUrl.StartsWith("http")) return;

			Task.Run(() =>
			{
				try
				{
					// Tải danh sách các mã bị khoá từ xa
					using var client = new WebClient();
					// Chống cache từ Windows và Google để luôn nhận dữ liệu mới nhất
					client.Headers.Add("Cache-Control", "no-cache");
					string url = RevokeListUrl + (RevokeListUrl.Contains("?") ? "&" : "?") + "_t=" + DateTime.Now.Ticks;
					string data = client.DownloadString(url);
					string hwId = GetHardwareId();

					// Nếu Hardware ID của máy này có trong danh sách bị khoá
					if (data.Contains(hwId))
					{
						LicenseInfo info = GetLicenseInfo();
						if (info.IsValid)
						{
							info.ExpirationDate = DateTime.MinValue; // Lập tức hết hạn
																	 // Không xoá AppliedKeys để tránh việc sử dụng lại key cũ khi được gỡ blocklist
							SaveLicenseInfo(info);
						}
					}
				}
				catch { /* Bỏ qua nếu không có mạng */ }
			});
		}

		public static string GetHardwareId()
		{
			string cpuId = GetWmiProperty("Win32_Processor", "ProcessorId");
			string boardId = GetWmiProperty("Win32_BaseBoard", "SerialNumber");
			return ComputeMD5(cpuId + boardId).ToUpper();
		}

		private static string GetWmiProperty(string wmiclass, string property)
		{
			try
			{
				using ManagementObjectSearcher searcher = new($"SELECT {property} FROM {wmiclass}");
				foreach (ManagementBaseObject obj in searcher.Get())
				{
					return obj[property]?.ToString()?.Trim() ?? "";
				}
			}
			catch { }
			return "UNKNOWN";
		}

		private static string ComputeMD5(string input)
		{
			using MD5 md5 = MD5.Create();
			byte[] bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
			StringBuilder sb = new();
			foreach (byte b in bytes) sb.Append(b.ToString("x2"));
			return sb.ToString();
		}

		public static LicenseInfo GetLicenseInfo()
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
				// First time run
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
				string decrypted = Decrypt(encryptedData);
				var info = LicenseInfo.Deserialize(decrypted);

				if (info.HardwareId != hwId)
				{
					info.IsHardwareChanged = true;
				}
				return info;
			}
			catch
			{
				// Tampered or corrupted
				return new LicenseInfo { HardwareId = hwId, ExpirationDate = DateTime.MinValue, LastRunDate = DateTime.Now, IsHardwareChanged = true };
			}
		}

		public static void SaveLicenseInfo(LicenseInfo info)
		{
			try
			{
				string serialized = info.Serialize();
				string encrypted = Encrypt(serialized);
				using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath);
				key?.SetValue("LicenseData", encrypted);
			}
			catch { }
		}

		public static void UpdateLastRunDate(LicenseInfo info)
		{
			if (info.IsValid && DateTime.Now >= info.LastRunDate)
			{
				info.LastRunDate = DateTime.Now;
				SaveLicenseInfo(info);
			}
		}

		public static bool ActivateLicense(string activationKey, out string message)
		{
			try
			{
				string rawKey = Encoding.UTF8.GetString(Convert.FromBase64String(activationKey));
				string[] parts = rawKey.Split('|');
				if (parts.Length != 4)
				{
					message = "Mã kích hoạt không hợp lệ.";
					return false;
				}

				string keyHwId = parts[0];
				int days = int.Parse(parts[1]);
				string randomGuid = parts[2];
				string providedSig = parts[3];

				string expectedSig = ComputeSHA256String($"{keyHwId}|{days}|{randomGuid}|{SecretKey}").Substring(0, 16);
				if (providedSig != expectedSig)
				{
					message = "Mã kích hoạt không hợp lệ hoặc đã bị chỉnh sửa.";
					return false;
				}

				LicenseInfo info = GetLicenseInfo();

				if (info.HardwareId != keyHwId)
				{
					message = "Mã kích hoạt này không dành cho máy tính (phần cứng) này.";
					return false;
				}

				if (info.AppliedKeys.Contains(activationKey))
				{
					message = "Mã kích hoạt này đã được sử dụng rồi.";
					return false;
				}

				if (info.IsHardwareChanged)
				{
					info.HardwareId = GetHardwareId();
					info.IsHardwareChanged = false;
				}

				// If already expired, base date is today. If still active, extend it.
				DateTime baseDate = DateTime.Now > info.ExpirationDate ? DateTime.Now : info.ExpirationDate;

				if (days >= 9999)
				{
					info.ExpirationDate = DateTime.MaxValue; // Permanent
				}
				else
				{
					info.ExpirationDate = baseDate.AddDays(days);
				}

				info.AppliedKeys.Add(activationKey);
				info.LastRunDate = DateTime.Now;
				SaveLicenseInfo(info);

				message = $"Kích hoạt thành công!\n\nHạn sử dụng mới: {(info.ExpirationDate == DateTime.MaxValue ? "Vĩnh viễn" : info.ExpirationDate.ToString("dd/MM/yyyy HH:mm"))}";
				return true;
			}
			catch
			{
				message = "Mã kích hoạt không hợp lệ hoặc bị lỗi định dạng.";
				return false;
			}
		}

		public static string GenerateKey(string hwId, int days)
		{
			string guid = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();
			string data = $"{hwId}|{days}|{guid}";
			string signature = ComputeSHA256String($"{data}|{SecretKey}").Substring(0, 16);
			string rawKey = $"{data}|{signature}";
			return Convert.ToBase64String(Encoding.UTF8.GetBytes(rawKey));
		}

		private static string ComputeSHA256String(string text)
		{
			using SHA256 sha256 = SHA256.Create();
			byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(text));
			StringBuilder sb = new();
			foreach (byte b in bytes) sb.Append(b.ToString("X2"));
			return sb.ToString();
		}

		private static byte[] GetHashSha256(string text)
		{
			using SHA256 sha256 = SHA256.Create();
			return sha256.ComputeHash(Encoding.UTF8.GetBytes(text));
		}

		private static string Encrypt(string plainText)
		{
			byte[] iv = new byte[16];
			using Aes aes = Aes.Create();
			aes.Key = GetHashSha256(SecretKey);
			aes.IV = iv; // For simplicity in this non-critical app
			ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);

			using MemoryStream ms = new();
			using (CryptoStream cs = new(ms, encryptor, CryptoStreamMode.Write))
			{
				using StreamWriter sw = new(cs);
				sw.Write(plainText);
			}
			return Convert.ToBase64String(ms.ToArray());
		}

		private static string Decrypt(string cipherText)
		{
			byte[] iv = new byte[16];
			byte[] buffer = Convert.FromBase64String(cipherText);

			using Aes aes = Aes.Create();
			aes.Key = GetHashSha256(SecretKey);
			aes.IV = iv;
			ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

			using MemoryStream ms = new(buffer);
			using CryptoStream cs = new(ms, decryptor, CryptoStreamMode.Read);
			using StreamReader sr = new(cs);
			return sr.ReadToEnd();
		}
	}
}
