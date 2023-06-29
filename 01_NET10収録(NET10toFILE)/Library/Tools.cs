using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.NetworkInformation;
using System.Drawing;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Windows.Controls;

namespace KAISYOU
{
    static internal class Tools
    {
        ///<summary>
        /// Pingの送信（01 NET10収録(NET10->FILE)がベース）
        ///</summary>
        /// <param name="ipAddress">送信先IPアドレス</param>
        /// <param name="timeOut">Pingタイムアウト時間[ms]</param>
        /// <returns>true = 送信成功、false = 送信失敗</returns>
        static public bool sendPing( string ipAddress, int timeOut )
        {
            try
            {
                // 送信データの作成
                string data = "ECHO Data";
                byte[] buffer = Encoding.ASCII.GetBytes(data);

                // Pingの設定（この設定値でよいか分かりません。）
                // 第一引数：ICMP エコー メッセージ パケットの有効期間 (TTL: time-to-live) = 32
                // 第二引数：ICMP エコー メッセージ パケットのフラグメンテーション = 許可
                PingOptions options = new PingOptions(32, false);

                // pingを実際に送信する
                Ping sender = new Ping();
                PingReply reply = sender.Send(ipAddress, timeOut, buffer, options);
                if( reply.Status == IPStatus.Success )
                {
                    // 送信成功
                    Debug.WriteLine("sendPing : Reply from {0}:bytes={1} time={2}ms TTL={3}",
                        reply.Address, reply.Buffer.Length,
                        reply.RoundtripTime, reply.Options.Ttl);
                }
                else
                {
                    // 送信失敗
                    //Debug.WriteLine( "sendPing : Send ping failed, " + reply.Status );
                    return false;
                }
            }
            catch( Exception exception )
            {
                Debug.WriteLine("sendPing : Exception occurred" + exception);
                return false;
            }

            return true;
        }

        ///<summary>
        /// <para>Pingの送信</para>
        /// <para>(NetToolsのようにPing送信失敗時に繰り返しPingを送信します。)</para>
        ///</summary>
        /// <param name="ipAddress">送信先IPアドレス</param>
        /// <param name="timeOut">初回のPingタイムアウト時間[ms]</param>
        /// <param name="nextTimeout">2回目以降のPingタイムアウト時間[ms]</param>
        /// <param name="repeatCount">Pingの送信回数</param>
        /// <returns>true = 送信成功、false = 送信失敗</returns>
        static public bool sendPing( string ipAddress, int timeOut, int nextTimeout, int repeatCount )
        {
            bool ret = false;
            for( int i = 0; i < repeatCount; i++ )
            {
                // Pingの送信（初回のみタイムアウト時間が違います）
                if( i == 0 )
                    ret = sendPing(ipAddress, timeOut);
                else
                    ret = sendPing(ipAddress, nextTimeout);

                // Pingの送信に成功したなら終了
                if( ret == true )
                    break;
            }

            return ret;
        }

        ///<summary>
        /// BGR値に対応するColor構造体を返す
        ///</summary>
        /// <param name="color">カラー値（BGR）</param>
        /// <returns>BGR値に対応するColor構造体</returns>
        //static public Color getColorFromBGR( int color )
        //{
        //    Byte R, G, B;

        //    B = (Byte)( ( color & 0xff0000 ) >> 16 );
        //    G = (Byte)( ( color & 0x00ff00 ) >> 8 );
        //    R = (Byte)( ( color & 0x0000ff ) >> 0 );

        //    return Color.FromArgb(R, G, B);
        //}

        /// <summary>
        /// バーが徐々に伸びるアニメーションを無効にして、
        /// ProgressBarのValueに値を設定する。
        /// http://dobon.net/vb/dotnet/control/pbdisableanimation.html
        /// </summary>
        /// <param name="pb">値を設定するProgressBar</param>
        /// <param name="val">設定する値</param>
        public static void SetProgressBarValue( ProgressBar pb, int val )
        {
            if( pb.Value < val )
            {
                //値を増やす時
                if( val < pb.Maximum )
                {
                    //目的の値より一つ大きくしてから、目的の値にする
                    pb.Value = val + 1;
                    pb.Value = val;
                }
                else
                {
                    //最大値にする時
                    //最大値を1つ増やしてから、元に戻す
                    pb.Maximum++;
                    pb.Value = val + 1;
                    pb.Value = val;
                    pb.Maximum--;
                }
            }
            else
            {
                //値を減らす時は、そのまま
                pb.Value = val;
            }
        }

        /// <summary>
        /// <para>指定されたファイルがロックされているかどうかを返します。</para>
        /// <para> URL:http://garafu.blogspot.jp/2015/01/c_11.html </para>
        /// </summary>
        /// <param name="path">検証したいファイルへのフルパス</param>
        /// <returns>ロックされているかどうか</returns>
        public static bool IsFileLocked( string path )
        {
            FileStream stream = null;

            try
            {
                stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch( FileNotFoundException exp )
            {
                Debug.Write(exp.Message);
                return false;
            }
            catch
            {
                return true;
            }
            finally
            {
                if( stream != null )
                {
                    stream.Close();
                }
            }

            return false;
        }

        /// <summary>
        /// 構造体からバイト配列に変換する
        /// </summary>
        /// <typeparam name="TStruct"></typeparam>
        /// <param name="s">変換したい構造体データ</param>
        /// <returns>変換後のバイト配列データ</returns>
        public static byte[] StructToByte<TStruct>( TStruct s ) where TStruct : struct
        {
            byte[] buffer = new byte[Marshal.SizeOf(typeof(TStruct))];
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);

            try
            {
                Marshal.StructureToPtr(s, handle.AddrOfPinnedObject(), false);
            }
            finally
            {
                handle.Free();
            }

            return buffer;
        }

        /// <summary>
        /// バイト配列から構造体に変換する
        /// </summary>
        /// <typeparam name="TStruct"></typeparam>
        /// <param name="b">変換したいバイト配列データ</param>
        /// <returns>変換後の構造体データ</returns>
        public static TStruct ByteToStruct<TStruct>( byte[] b ) where TStruct : struct
        {
            var handle = GCHandle.Alloc(b, GCHandleType.Pinned);

            try
            {
                return (TStruct)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(TStruct));
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>
        /// 文字列を指定したバイトサイズに切り詰める
        /// </summary>
        /// <param name="str">切り詰めたい文字列</param>
        /// <param name="byteSize">切り詰めるバイト数</param>
        /// <returns></returns>
        public static string TruncateString( string str, int byteSize )
        {
            Encoding enc = Encoding.GetEncoding("Shift_JIS");      // Shift-JISエンコーディング
            byte[] bStr = enc.GetBytes(str);    // 文字列をバイトに変換

            // 文字列のバイト数が指定値以上ならば、指定値に切り詰めて返す
            if( bStr.Length > byteSize )
                return enc.GetString(bStr, 0, byteSize);    // 指定したバイト数まで文字列を変換
            else
                return str;
        }

        /// <summary>
        /// XMLファイルからデータをデシリアライズする
        /// </summary>
        /// <typeparam name="T">デシリアライズするデータの型</typeparam>
        /// <param name="filePath">XMLファイルパス</param>
        /// <param name="data">デシリアライズするデータ</param>
        /// <returns>true : 成功、false : 失敗</returns>
        public static bool ReadSerializeFileXML<T>( string filePath, ref T data )
        {
            try
            {
                // ファイルの内容を元にデシリアライズする
                using( Stream stream = File.OpenRead(filePath) )
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(T));
                    data = (T)serializer.Deserialize(stream);
                }

                return true;
            }
            catch( Exception exp )
            {
                Debug.WriteLine(exp.StackTrace);
                Debug.WriteLine(exp.Message);
                return false;
            }
        }

        /// <summary>
        /// データをシリアライズしてXMLファイルに書く
        /// </summary>
        /// <typeparam name="T">シリアライズするデータの型</typeparam>
        /// <param name="filePath">XMLファイルパス</param>
        /// <param name="data">シリアライズするデータ</param>
        /// <returns>true : 成功、false : 失敗</returns>
        public static bool WriteSerializeFileXML<T>( string filePath, T data )
        {
            try
            {
                // データをシリアライズしてXMLに書き込む
                using( Stream stream = File.Open(filePath, FileMode.Create) )
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(T));
                    serializer.Serialize(stream, data);
                }

                return true;
            }
            catch( Exception exp )
            {
                Debug.WriteLine(exp.StackTrace);
                Debug.WriteLine(exp.Message);
                return false;
            }
        }

        /// <summary>
        /// XMLファイルからデータをデシリアライズする
        /// </summary>
        /// <typeparam name="T">デシリアライズするデータの型</typeparam>
        /// <param name="filePath">ファイルパス</param>
        /// <param name="data">デシリアライズするデータ</param>
        /// <returns>true : 成功、false : 失敗</returns>
        public static bool ReadSerializeFileBinary<T>( string filePath, ref T data )
        {
            try
            {
                // ファイルの内容を元にデシリアライズする
                using( Stream stream = File.OpenRead(filePath) )
                {
                    BinaryFormatter serializer = new BinaryFormatter();
                    data = (T)serializer.Deserialize(stream);
                }

                return true;
            }
            catch( Exception exp )
            {
                Debug.WriteLine(exp.StackTrace);
                Debug.WriteLine(exp.Message);
                return false;
            }
        }

        /// <summary>
        /// データをシリアライズしてXMLファイルに書く
        /// </summary>
        /// <typeparam name="T">シリアライズするデータの型</typeparam>
        /// <param name="filePath">ファイルパス</param>
        /// <param name="data">シリアライズするデータ</param>
        /// <returns>true : 成功、false : 失敗</returns>
        public static bool WriteSerializeFileBinary<T>( string filePath, T data )
        {
            try
            {
                // データをシリアライズしてXMLに書き込む
                using( Stream stream = File.Open(filePath, FileMode.Create) )
                {
                    BinaryFormatter serializer = new BinaryFormatter();
                    serializer.Serialize(stream, data);
                }

                return true;
            }
            catch( Exception exp )
            {
                Debug.WriteLine(exp.StackTrace);
                Debug.WriteLine(exp.Message);
                return false;
            }
        }

        /// <summary>バイナリシリアライズを使って任意の型Tのオブジェクトを複製する</summary>
        /// <returns><paramref name="source"/>を複製したオブジェクト</returns>
        /// <exception cref="SerializationException"><paramref name="source"/>がシリアル化可能としてマークされていない</exception>
        static public T CloneObject<T>( T source )
        {
            // バイナリシリアライズによってsourceの複製を作成する
            using( MemoryStream stream = new MemoryStream() )
            {
                BinaryFormatter f = new BinaryFormatter();

                f.Serialize(stream, source);

                stream.Position = 0L;

                return (T)f.Deserialize(stream);
            }
        }

        /// <summary>
        /// WORDデータの指定ビットのチェック
        /// </summary>
        /// <param name="Dat"></param>
        /// <param name="Bit"></param>
        /// <returns></returns>
        static public bool CheckBit( short Dat, int Bit )
        {
            if( ( Bit < 0 ) || ( 15 < Bit ) )
                return false;

            if( ( ( Dat & (short)Math.Pow(2, Bit) ) >> Bit ) == 0 )
                return false;
            else
                return true;
        }
    }
}
