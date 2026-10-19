package;

class Crypto {
    public static function createSimpleAuthToken(sessionId:String, secretKey:String):String {
        var payload = sessionId + ":" + Date.now().getTime();
        var signature = UrlUtils.crc32(payload + secretKey);
        return payload + "." + StringTools.hex(signature);
    }

    public static function verifySimpleAuthToken(token:String, secretKey:String):Bool {
        if (token == null || token.indexOf(".") == -1) return false;
        var parts = token.split(".");
        if (parts.length != 2) return false;
        var payload = parts[0];
        var sigHex = parts[1];
        var expectedSig = StringTools.hex(UrlUtils.crc32(payload + secretKey));
        return sigHex.toUpperCase() == expectedSig.toUpperCase();
    }
}
