/**
 * ============================================================
 *  TPL License Key Generator — Google Apps Script
 *  Phiên bản dành cho Admin (tạo mã kích hoạt)
 * ============================================================
 *
 * Hướng dẫn triển khai:
 *   1. Vào https://script.google.com/
 *   2. Tạo project mới, paste file Code.gs và Index.html
 *   3. Deploy → Web App → Execute as: Me, Who has access: Anyone
 *   4. Dùng link web app để sinh mã kích hoạt
 *
 * ⚠️  QUAN TRỌNG: SecretKey phải GIỐNG HỆT trong LicenseManager.cs
 * ============================================================
 */

// ─── CẤU HÌNH (phải khớp với LicenseManager.cs) ─────────────
var CONFIG = {
  // ⚠️  SecretKey PHẢI giống hệt LicenseManager.cs
  SECRET_KEY: "TPL_V1_SECRET_KEY_2026_NEVER_SHARE_THIS_EVER!!",
  // Alphabet Base32 (giống C#)
  BASE32_ALPHABET: "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"
};

// ─── DoGet — Web App / CSV endpoint ──────────────────────────
function doGet(e) {
  // Nếu có ?action=getRevokeCsv → trả về CSV cho C# client
  if (e && e.parameter && e.parameter.action === "getRevokeCsv") {
    var csv = getRevokeCsv();
    return ContentService.createTextOutput(csv)
      .setMimeType(ContentService.MimeType.CSV)
      .setCharset("UTF-8");
  }
  // Mặc định: trả về giao diện Web App
  return HtmlService.createHtmlOutputFromFile("Index")
    .setTitle("TPL License Key Generator")
    .setXFrameOptionsMode(HtmlService.XFrameOptionsMode.ALLOWALL);
}

// ─── Hàm chính để sinh key (gọi từ frontend) ─────────────────
function generateLicenseKey(hwId, days, seq) {
  // 1. Chuẩn hóa Hardware ID
  var fullHwId = hwId.replace(/-/g, "").toUpperCase();
  if (fullHwId.length < 8) throw new Error("Hardware ID phải có ít nhất 8 ký tự hex.");
  var shortHwIdHex = fullHwId.substring(0, 8).padEnd(8, "0");

  // 2. Chuyển shortHwIdHex (8 ký tự) → 4 bytes
  var shortHwIdBytes = [];
  for (var i = 0; i < 4; i++) {
    shortHwIdBytes.push(parseInt(shortHwIdHex.substring(i * 2, i * 2 + 2), 16));
  }

  // 3. Số ngày → 2 bytes big-endian
  var daysBytes = [
    (days >> 8) & 0xFF,
    days & 0xFF
  ];

  // 4. Sequence byte — tự động tạo nếu để trống và HWID đã từng bị thu hồi
  if (!seq || seq.trim() === "") {
    // Kiểm tra xem HWID này đã có key bị revoked chưa
    var allKeys = getKeysDb();
    var hasRevoked = false;
    for (var ki = 0; ki < allKeys.length; ki++) {
      if (allKeys[ki].fullHwId === fullHwId && allKeys[ki].revoked) {
        hasRevoked = true;
        break;
      }
    }
    if (hasRevoked) {
      // Tạo seq duy nhất dựa trên timestamp để đảm bảo key mới khác key cũ
      seq = "auto_" + new Date().getTime();
    }
  }

  var seqByte = 0;
  if (seq && seq.length > 0) {
    var sum = 0;
    for (var c = 0; c < seq.length; c++) {
      sum = (sum + seq.charCodeAt(c)) % 256;
    }
    seqByte = sum;
  }

  // 5. Tính chữ ký SHA256 (3 byte đầu)
  var textToHash = shortHwIdHex + "|" + days + "|" + seqByte + "|" + CONFIG.SECRET_KEY;
  var hashBytes = sha256(textToHash);
  var sigBytes = hashBytes.slice(0, 3);

  // 6. Ghép payload 10 bytes
  var payload = [];
  payload = payload.concat(daysBytes);        // 2 bytes
  payload = payload.concat(shortHwIdBytes);    // 4 bytes
  payload.push(seqByte);                       // 1 byte
  payload = payload.concat(sigBytes);          // 3 bytes

  // 7. Mã hóa Base32 → 16 ký tự
  var rawBase32 = base32Encode(payload);

  // 8. Format: XXXX-XXXX-XXXX-XXXX
  var formatted = "";
  for (var i = 0; i < 16; i++) {
    if (i > 0 && i % 4 === 0) formatted += "-";
    formatted += rawBase32[i];
  }

  // 9. Lưu vào database
  var expireText = days >= 9999 ? "Vĩnh viễn" : days + " ngày";
  var keyEntry = {
    id: Utilities.getUuid(),
    fullHwId: fullHwId,
    shortHwId: shortHwIdHex,
    key: formatted,
    rawKey: rawBase32,
    days: days,
    seq: seq || "",
    seqByte: seqByte,
    expires: expireText,
    generated: Utilities.formatDate(new Date(), Session.getScriptTimeZone(), "yyyy-MM-dd HH:mm"),
    revoked: false
  };
  addGeneratedKey(keyEntry);

  return {
    key: formatted,
    rawKey: rawBase32,
    shortHwId: shortHwIdHex,
    days: days,
    seqByte: seqByte,
    seq: seq || "",
    signature: bytesToHex(sigBytes),
    fullHash: bytesToHex(hashBytes),
    expires: expireText,
    keyId: keyEntry.id
  };
}

// ─── Base32 Encode (10 bytes → 16 ký tự) ─────────────────────
//  Thuật toán giống hệt C# LicenseManager.Base32.Encode
function base32Encode(data) {
  if (!data || data.length !== 10) {
    throw new Error("Data must be exactly 10 bytes.");
  }

  var alphabet = CONFIG.BASE32_ALPHABET;
  var chars = [];
  var byteIndex = 0;
  var bitBuffer = 0;
  var bitCount = 0;

  while (chars.length < 16) {
    if (bitCount < 5) {
      bitBuffer = (bitBuffer << 8) | (data[byteIndex] & 0xFF);
      byteIndex++;
      bitCount += 8;
    }
    var index = (bitBuffer >> (bitCount - 5)) & 0x1F;
    bitCount -= 5;
    chars.push(alphabet[index]);
  }

  return chars.join("");
}

// ─── SHA256 dùng Google Apps Script Utilities ────────────────
function sha256(text) {
  var bytes = Utilities.computeDigest(
    Utilities.DigestAlgorithm.SHA_256,
    text,
    Utilities.Charset.UTF_8
  );
  // computeDigest trả về mảng số nguyên có dấu (-128..127)
  // Chuyển về unsigned byte
  var result = [];
  for (var i = 0; i < bytes.length; i++) {
    result.push(bytes[i] & 0xFF);
  }
  return result;
}

// ─── Byte array → Hex string ─────────────────────────────────
function bytesToHex(bytes) {
  var hex = "";
  for (var i = 0; i < bytes.length; i++) {
    var b = bytes[i] & 0xFF;
    hex += (b < 16 ? "0" : "") + b.toString(16).toUpperCase();
  }
  return hex;
}

// ─── Hàm helper: Validate HWID ───────────────────────────────
function validateHwId(hwId) {
  var cleaned = hwId.replace(/-/g, "").toUpperCase();
  return /^[0-9A-F]{8,}$/.test(cleaned);
}

// ═════════════════════════════════════════════════════════════
//  QUẢN LÝ THU HỒI BẢN QUYỀN (REVOKE LIST)
// ═════════════════════════════════════════════════════════════
//
//  Dữ liệu lưu trong PropertiesService (Script Properties).
//  C# client gọi: ?action=getRevokeCsv → nhận CSV để kiểm tra.
//
//  Sau khi thay đổi danh sách, nhớ cập nhật RevokeListUrl
//  trong LicenseManager.cs thành:
//    https://script.google.com/.../exec?action=getRevokeCsv
//
//  ⚠️  CƠ CHẾ THU HỒI VĨNH VIỄN:
//  Khi C# client phát hiện HWID/KEY khớp với revoke list,
//  nó đặt flag IsPermanentlyRevoked=true trong registry local.
//  Flag này KHÔNG BAO GIỜ bị xoá — kể cả khi admin gỡ mục đó
//  khỏi revoke list. Do đó:
//    - Key cũ → không thể kích hoạt lại (đã nằm trong AppliedKeys)
//    - Key mới cho cùng HWID → cũng bị chặn (IsPermanentlyRevoked)
//    - Gỡ thu hồi → không phục hồi được license cũ
//  Muốn phục hồi → user phải xoá registry hoặc cài lại Windows.
// ═════════════════════════════════════════════════════════════

var REVOKE_PROP_KEY = "TPL_REVOKE_LIST";

/**
 * Lấy danh sách thu hồi từ PropertiesService.
 */
function getRevokeList() {
  var props = PropertiesService.getScriptProperties();
  var data = props.getProperty(REVOKE_PROP_KEY);
  if (!data) return [];
  return JSON.parse(data);
}

/**
 * Lưu danh sách thu hồi vào PropertiesService.
 */
function saveRevokeList(list) {
  var props = PropertiesService.getScriptProperties();
  props.setProperty(REVOKE_PROP_KEY, JSON.stringify(list));
}

/**
 * Thêm một mục vào danh sách thu hồi.
 * @param {string} type  "HWID" hoặc "KEY"
 * @param {string} value Hardware ID hoặc mã kích hoạt
 * @param {string} reason  Lý do thu hồi (tuỳ chọn)
 */
function addRevoke(type, value, reason) {
  if (!value || !value.trim()) throw new Error("Vui lòng nhập giá trị cần thu hồi.");

  var cleaned = value.replace(/-/g, "").replace(/\s/g, "").toUpperCase();
  if (type === "HWID" && !/^[0-9A-F]{8,}$/.test(cleaned)) {
    throw new Error("Hardware ID không hợp lệ. Phải là chuỗi hex ít nhất 8 ký tự.");
  }
  if (type === "KEY" && cleaned.length !== 16) {
    throw new Error("Mã kích hoạt phải đúng 16 ký tự (sau khi loại bỏ dấu gạch).");
  }

  var list = getRevokeList();

  // Kiểm tra trùng lặp
  for (var i = 0; i < list.length; i++) {
    if (list[i].value === cleaned) {
      throw new Error("Giá trị '" + cleaned + "' đã có trong danh sách thu hồi.");
    }
  }

  list.push({
    type: type,
    value: cleaned,
    original: value.trim(),
    reason: reason || "",
    timestamp: new Date().toISOString(),
    revokeDate: Utilities.formatDate(new Date(), Session.getScriptTimeZone(), "yyyy-MM-dd HH:mm")
  });

  saveRevokeList(list);
  return { success: true, count: list.length };
}

/**
 * Xoá một mục khỏi danh sách thu hồi theo index.
 */
function removeRevoke(index) {
  var list = getRevokeList();
  if (index < 0 || index >= list.length) throw new Error("Index không hợp lệ.");
  var removed = list.splice(index, 1);
  saveRevokeList(list);
  return { success: true, count: list.length, removed: removed[0] };
}

/**
 * Xoá tất cả danh sách thu hồi.
 */
function clearAllRevokes() {
  saveRevokeList([]);
  return { success: true, count: 0 };
}

/**
 * Lấy danh sách thu hồi (cho frontend).
 */
function listRevokes() {
  return getRevokeList();
}

/**
 * Tạo CSV cho C# client.
 * Mỗi dòng là một HWID hoặc KEY (đã clean).
 * Định dạng đơn giản để C# dùng Contains() kiểm tra.
 */
function getRevokeCsv() {
  var list = getRevokeList();
  var lines = [];
  lines.push("# TPL Revoke List - Generated " + new Date().toISOString());
  lines.push("# TYPE,VALUE,REASON,DATE");
  for (var i = 0; i < list.length; i++) {
    var item = list[i];
    // Ghi cả value gốc + type để dễ debug
    lines.push(item.value);
    // Dòng comment chứa thông tin chi tiết
    lines.push("# " + item.type + " | " + item.reason + " | " + item.revokeDate);
  }
  return lines.join("\n");
}

/**
 * Đếm số lượng mục đã thu hồi.
 */
function getRevokeStats() {
  var list = getRevokeList();
  var hwidCount = 0, keyCount = 0;
  for (var i = 0; i < list.length; i++) {
    if (list[i].type === "HWID") hwidCount++;
    else keyCount++;
  }
  return {
    total: list.length,
    hwidCount: hwidCount,
    keyCount: keyCount
  };
}

// ═════════════════════════════════════════════════════════════
//  CƠ SỞ DỮ LIỆU KEY ĐÃ SINH (Generated Keys DB)
// ═════════════════════════════════════════════════════════════
//
//  Lưu tất cả key đã sinh để admin có thể quản lý thu hồi
//  trực quan bằng checkbox. Mỗi key lưu kèm HWID, ngày, seq...
// ═════════════════════════════════════════════════════════════

var KEYS_DB_PROP_KEY = "TPL_GENERATED_KEYS";

function getKeysDb() {
  var props = PropertiesService.getScriptProperties();
  var data = props.getProperty(KEYS_DB_PROP_KEY);
  if (!data) return [];
  return JSON.parse(data);
}

function saveKeysDb(list) {
  var props = PropertiesService.getScriptProperties();
  props.setProperty(KEYS_DB_PROP_KEY, JSON.stringify(list));
}

function addGeneratedKey(entry) {
  // Gán note từ key cùng HWID gần nhất nếu có
  var list = getKeysDb();
  var lastNote = "";
  for (var i = list.length - 1; i >= 0; i--) {
    if (list[i].fullHwId === entry.fullHwId && list[i].note) {
      lastNote = list[i].note;
      break;
    }
  }
  entry.note = entry.note || lastNote || "";
  list.push(entry);
  saveKeysDb(list);
}

/**
 * Lấy tất cả key đã sinh (cho frontend).
 */
function getAllGeneratedKeys() {
  return getKeysDb();
}

/**
 * Toggle trạng thái revoked của một key trong database.
 * Đồng thời thêm/xoá khỏi revoke list.
 * @param {string} keyId   UUID của key entry
 * @param {boolean} revoked true = thu hồi, false = bỏ thu hồi
 * @param {string} reason   Lý do (khi revoke)
 */
function toggleKeyRevoke(keyId, revoked, reason) {
  var keys = getKeysDb();
  var found = null;
  for (var i = 0; i < keys.length; i++) {
    if (keys[i].id === keyId) {
      found = keys[i];
      break;
    }
  }
  if (!found) throw new Error("Key không tồn tại trong database.");

  found.revoked = revoked;
  saveKeysDb(keys);

  if (revoked) {
    // Thêm vào revoke list (cả HWID và KEY)
    var rl = getRevokeList();

    // Thêm HWID (nếu chưa có)
    var hwExists = false;
    var keyExists = false;
    for (var j = 0; j < rl.length; j++) {
      if (rl[j].type === "HWID" && rl[j].value === found.fullHwId) hwExists = true;
      if (rl[j].type === "KEY" && rl[j].value === found.rawKey) keyExists = true;
    }

    if (!hwExists) {
      rl.push({
        type: "HWID",
        value: found.fullHwId,
        original: found.fullHwId,
        reason: "[Auto] " + (reason || "Thu hồi từ danh sách key"),
        timestamp: new Date().toISOString(),
        revokeDate: Utilities.formatDate(new Date(), Session.getScriptTimeZone(), "yyyy-MM-dd HH:mm")
      });
    }
    // Luôn thêm key cụ thể (để chặn chính xác key đó)
    if (!keyExists) {
      rl.push({
        type: "KEY",
        value: found.rawKey,
        original: found.key,
        reason: "[Auto] " + (reason || "Thu hồi từ danh sách key"),
        timestamp: new Date().toISOString(),
        revokeDate: Utilities.formatDate(new Date(), Session.getScriptTimeZone(), "yyyy-MM-dd HH:mm")
      });
    }

    saveRevokeList(rl);
  } else {
    // Bỏ thu hồi: xoá HWID và KEY khỏi revoke list
    var rl = getRevokeList();
    var newRl = [];
    for (var j = 0; j < rl.length; j++) {
      // Giữ lại những mục không khớp với HWID hoặc KEY này
      var isHwMatch = (rl[j].type === "HWID" && rl[j].value === found.fullHwId);
      var isKeyMatch = (rl[j].type === "KEY" && rl[j].value === found.rawKey);
      // Chỉ xoá nếu lý do là [Auto] (không xoá các mục thủ công)
      var isAuto = rl[j].reason && rl[j].reason.indexOf("[Auto]") === 0;
      if ((isHwMatch || isKeyMatch) && isAuto) {
        continue; // bỏ qua → xoá
      }
      newRl.push(rl[j]);
    }
    saveRevokeList(newRl);
  }

  return {
    success: true,
    revoked: revoked,
    key: found.key,
    hwId: found.fullHwId
  };
}

/**
 * Xoá một key entry khỏi database.
 * Nếu key đang bị revoked, cũng xoá khỏi revoke list.
 */
function deleteKeyEntry(keyId) {
  var keys = getKeysDb();
  var foundIndex = -1;
  var found = null;
  for (var i = 0; i < keys.length; i++) {
    if (keys[i].id === keyId) {
      foundIndex = i;
      found = keys[i];
      break;
    }
  }
  if (foundIndex === -1) throw new Error("Key không tồn tại.");

  // Nếu key đang bị revoked, xoá khỏi revoke list
  if (found.revoked) {
    var rl = getRevokeList();
    var newRl = [];
    for (var j = 0; j < rl.length; j++) {
      var isHwMatch = (rl[j].type === "HWID" && rl[j].value === found.fullHwId);
      var isKeyMatch = (rl[j].type === "KEY" && rl[j].value === found.rawKey);
      if ((isHwMatch || isKeyMatch) && rl[j].reason && rl[j].reason.indexOf("[Auto]") === 0) {
        continue;
      }
      newRl.push(rl[j]);
    }
    saveRevokeList(newRl);
  }

  keys.splice(foundIndex, 1);
  saveKeysDb(keys);
  return { success: true };
}

/**
 * Xoá tất cả key của một HWID.
 */
function deleteKeysForHwId(fullHwId) {
  var keys = getKeysDb();
  var rl = getRevokeList();

  // Xoá khỏi revoke list
  var newRl = [];
  for (var j = 0; j < rl.length; j++) {
    var isHwMatch = (rl[j].type === "HWID" && rl[j].value === fullHwId);
    var isAuto = rl[j].reason && rl[j].reason.indexOf("[Auto]") === 0;
    if (isHwMatch && isAuto) continue;
    // Cũng xoá các key [Auto] thuộc HWID này
    if (rl[j].type === "KEY" && isAuto) {
      var belongsToHw = false;
      for (var k = 0; k < keys.length; k++) {
        if (keys[k].fullHwId === fullHwId && keys[k].rawKey === rl[j].value) {
          belongsToHw = true; break;
        }
      }
      if (belongsToHw) continue;
    }
    newRl.push(rl[j]);
  }
  saveRevokeList(newRl);

  // Xoá keys
  var remaining = [];
  for (var i = 0; i < keys.length; i++) {
    if (keys[i].fullHwId !== fullHwId) {
      remaining.push(keys[i]);
    }
  }
  saveKeysDb(remaining);
  return { success: true, deleted: keys.length - remaining.length };
}

/**
 * Cập nhật ghi chú cho một HWID (áp dụng cho tất cả key cùng HWID).
 */
function updateHwIdNote(fullHwId, note) {
  var keys = getKeysDb();
  var updated = 0;
  for (var i = 0; i < keys.length; i++) {
    if (keys[i].fullHwId === fullHwId) {
      keys[i].note = note;
      updated++;
    }
  }
  if (updated === 0) throw new Error("Không tìm thấy HWID nào.");
  saveKeysDb(keys);
  return { success: true, updated: updated };
}
