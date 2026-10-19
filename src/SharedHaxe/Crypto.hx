package;

@:keep
class Crypto {
    public static function createSimpleAuthToken(sessionId:String, secretKey:String):String {
        var timestamp = Math.floor(Date.now().getTime());
        var payload = sessionId + ":" + timestamp;
        var signature = UrlUtils.crc32(payload + secretKey);
        return payload + "." + StringTools.hex(signature);
    }

    public static function verifySimpleAuthToken(token:String, secretKey:String):Bool {
        if (token == null) return false;
        var lastDot = token.lastIndexOf(".");
        if (lastDot <= 0 || lastDot == token.length - 1) return false;

        var payload = token.substring(0, lastDot);
        var sigHex = token.substring(lastDot + 1);
        var expectedSig = StringTools.hex(UrlUtils.crc32(payload + secretKey));
        return sigHex.toUpperCase() == expectedSig.toUpperCase();
    }
}
