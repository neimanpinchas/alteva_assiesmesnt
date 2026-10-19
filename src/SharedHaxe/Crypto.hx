package;

import haxe.crypto.Hmac;
import haxe.io.Bytes;

@:keep
class Crypto {
    public static function createSimpleAuthToken(sessionId:String, secretKey:String):String {
        var timestamp = Math.floor(Date.now().getTime());
        var payload = sessionId + ":" + timestamp;
        var hmac = new Hmac(SHA256);
        var secretBytes = Bytes.ofString(secretKey);
        var payloadBytes = Bytes.ofString(payload);
        var signature = hmac.make(secretBytes, payloadBytes).toHex();
        return payload + "." + signature;
    }

    public static function verifySimpleAuthToken(token:String, secretKey:String):Bool {
        if (token == null) return false;
        var lastDot = token.lastIndexOf(".");
        if (lastDot <= 0 || lastDot == token.length - 1) return false;

        var payload = token.substring(0, lastDot);
        var sigHex = token.substring(lastDot + 1);
        var hmac = new Hmac(SHA256);
        var secretBytes = Bytes.ofString(secretKey);
        var payloadBytes = Bytes.ofString(payload);
        var expectedSig = hmac.make(secretBytes, payloadBytes).toHex();
        return sigHex.toLowerCase() == expectedSig.toLowerCase();
    }
}
