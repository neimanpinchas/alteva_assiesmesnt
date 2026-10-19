package;

@:keep
class UrlUtils {
    public static function crc32(str:String):Int {
        var crc:Int = 0xFFFFFFFF;
        for (i in 0...str.length) {
            var byte = str.charCodeAt(i);
            crc = crc ^ byte;
            for (j in 0...8) {
                var mask = -(crc & 1);
                crc = (crc >>> 1) ^ (0xEDB88320 & mask);
            }
        }
        return (crc ^ 0xFFFFFFFF) & 0xFFFFFFFF;
    }

    public static function extractDomain(urlStr:String):String {
        if (urlStr == null || urlStr.length == 0) return "";
        var clean = urlStr;
        var schemeIdx = clean.indexOf("://");
        if (schemeIdx >= 0) {
            clean = clean.substring(schemeIdx + 3);
        }
        var slashIdx = clean.indexOf("/");
        if (slashIdx >= 0) {
            clean = clean.substring(0, slashIdx);
        }
        var colonIdx = clean.indexOf(":");
        if (colonIdx >= 0) {
            clean = clean.substring(0, colonIdx);
        }
        return clean.toLowerCase();
    }
}
