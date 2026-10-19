package;

import haxe.Int64;
import haxe.crypto.Sha256;
import haxe.io.Bytes;

@:keep
class SimHash {
    public static function hammingDistance64(h1:Int64, h2:Int64):Int {
        var x = h1 ^ h2;
        var count = 0;
        for (i in 0...64) {
            var bit:Int64 = Int64.make(0, 1) << i;
            if (!((x & bit) == Int64.make(0, 0))) {
                count++;
            }
        }
        return count;
    }

    public static function hammingDistance(h1:Float, h2:Float):Int {
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

    public static function computeSimHash64(text:String):Int64 {
        if (text == null || text.length == 0) return Int64.make(0, 0);
        var words = text.split(" ");
        var v = [for (i in 0...64) 0];

        for (w in words) {
            if (w.length == 0) continue;
            var shaBytes = Sha256.make(Bytes.ofString(w));
            var hHigh:Int = (shaBytes.get(0) << 24) | (shaBytes.get(1) << 16) | (shaBytes.get(2) << 8) | shaBytes.get(3);
            var hLow:Int = (shaBytes.get(4) << 24) | (shaBytes.get(5) << 16) | (shaBytes.get(6) << 8) | shaBytes.get(7);
            var hash:Int64 = Int64.make(hHigh, hLow);

            for (i in 0...64) {
                var bit:Int64 = Int64.make(0, 1) << i;
                if (!((hash & bit) == Int64.make(0, 0))) {
                    v[i] += 1;
                } else {
                    v[i] -= 1;
                }
            }
        }

        var fingerPrint:Int64 = Int64.make(0, 0);
        for (i in 0...64) {
            if (v[i] > 0) {
                fingerPrint = fingerPrint | (Int64.make(0, 1) << i);
            }
        }
        return fingerPrint;
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
