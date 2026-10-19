package;

import haxe.Int64;
import haxe.io.Bytes;

@:keep
class SimHash {
    private static function makeBit(i:Int):Int64 {
        if (i < 32) {
            return Int64.make(0, 1 << i);
        } else {
            return Int64.make(1 << (i - 32), 0);
        }
    }

    public static function crc64(str:String):Int64 {
        var poly:Int64 = Int64.make(0x42F0E1EB, 0xA9EA3693);
        var crc:Int64 = Int64.make(0, 0);
        var bytes = Bytes.ofString(str);
        var mask:Int64 = Int64.make(0x80000000, 0);
        var zero:Int64 = Int64.make(0, 0);

        for (b in 0...bytes.length) {
            var byteVal = bytes.get(b);
            var byte64:Int64 = Int64.make(byteVal << 24, 0);
            crc = crc ^ byte64;
            for (i in 0...8) {
                var topBitSet = (crc & mask) != zero;
                crc = crc << 1;
                if (topBitSet) {
                    crc = crc ^ poly;
                }
            }
        }
        return crc;
    }

    public static function hammingDistance64(h1:Int64, h2:Int64):Int {
        var x = h1 ^ h2;
        var count = 0;
        var zero:Int64 = Int64.make(0, 0);
        for (i in 0...64) {
            var bit:Int64 = makeBit(i);
            if ((x & bit) != zero) {
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
        var lower = text.toLowerCase();
        var r = ~/\w+/g;
        var words:Array<String> = [];
        if (r.match(lower)) {
            do {
                words.push(r.matched(0));
            } while (r.matchSub(lower, r.matchedPos().pos + r.matchedPos().len));
        }
        if (words.length == 0) return Int64.make(0, 0);

        var v = [for (i in 0...64) 0];

        for (w in words) {
            var hash:Int64 = crc64(w);

            for (i in 0...64) {
                var bit:Int64 = makeBit(i);
                if ((hash & bit) != Int64.make(0, 0)) {
                    v[i] += 1;
                } else {
                    v[i] -= 1;
                }
            }
        }

        var fingerPrint:Int64 = Int64.make(0, 0);
        for (i in 0...64) {
            if (v[i] > 0) {
                fingerPrint = fingerPrint | makeBit(i);
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
