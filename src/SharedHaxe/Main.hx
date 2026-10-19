package;

class Main {
    public static function main() {
        var domain = UrlUtils.extractDomain("https://example.com/test");
        var hash = UrlUtils.crc32(domain);
        trace("Shared Haxe Initialized. Example domain: " + domain + ", Hash: " + hash);
    }
}
