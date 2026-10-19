package;

class SimHash {
    public static function hammingDistance(h1:Float, h2:Float):Int {
        // Hamming distance helper for 32-bit integer representations
        var i1:Int = Std.int(h1);
        var i2:Int = Std.int(h2);
        var x = i1 ^ i2;
        var count = 0;
        while (x != 0) {
            count += (x & 1);
            x = x >>> 1;
        }
        return count;
    }

    public static function computeSimHash32(text:String):Int {
        if (text == null || text.length == 0) return 0;
        var words = text.split(" ");
        var v = [for (i in 0...32) 0];
        
        for (w in words) {
            if (w.length == 0) continue;
            var hash = UrlUtils.crc32(w);
            for (i in 0...32) {
                if ((hash & (1 << i)) != 0) {
                    v[i] += 1;
                } else {
                    v[i] -= 1;
                }
            }
        }

        var fingerPrint:Int = 0;
        for (i in 0...32) {
            if (v[i] > 0) {
                fingerPrint |= (1 << i);
            }
        }
        return fingerPrint;
    }
}
