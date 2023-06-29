using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Microsoft.VisualBasic.FileIO; // 文字列のパース

using static System.Convert;

// NET10アドレスの区分Index
enum NET10_DIV {
    DTMaster = 0,
    Net10Data,
}

// NET10アドレス情報(アプリ参照用)
public struct NET10_AddressInfo
{
    public short[] SAddr;       // 開始アドレス
    public short[] EAddr;       // 終了アドレス
    public short[] DType;       // 不明（未使用の模様）
    public short[] DCnt;        // 終了アドレス<->開始アドレス間のデータ点数（WORD）
    public short BlockCount;    // アドレスブロック数（SAddrの配列数）
    public int[,] DelimitNo;    // NET10の受信バッファの各区分の開始、終了位置
                                // 行：区分（NET10_DIV）
                                // 列：0=開始位置、1=終了位置
}

// NET10アドレス情報(ファイル読み出し用)
public struct NET10_AddressFileInfo
{
    public List<short> SAddr;   // 開始アドレス
    public List<short> EAddr;   // 終了アドレス
}

namespace _01_NET10収録_NET10toFILE_
{
    //********************************************************************************
    // MELSECNET/Hの受信アドレス管理クラス
    // 受信アドレスはCSVファイルより読み出しを行う
    //********************************************************************************
    public class NET10Address
    {
        // 定数
        // NET10アドレスファイルの格納フォルダ（実行ファイル内）
        // ビルド時にプロジェクトフォルダ内の「RequiredFiles」フォルダからコピーするので、
        // 変更時は「RequiredFiles」の内容を変更してください。
        static readonly string    ADDRESS_FOLDER = "\\Address\\";

        // NET10アドレスファイル名
        static readonly string[] ADDRESS_FILENAME =
        {
            "", // DTMasterはアドレスが無い
            "Address_Net10Data.csv",
        };

        // 浚渫ID、油回収ID関連の参照アドレス
        static readonly short SMODE_ADDRESS = 0x12EF;    // W12F1 浚渫中（通常 0、浚渫中 1)
                                                         // 参照アドレスはW12EFだが、実際の判定処理で+2しておりW12F1を見ている
        static readonly short OMODE_ADDRESS = 0x0A03;    // W0A03 3bit 推進モード 自動油回収

        // 変数
        public NET10_AddressInfo m_AddrInfo;            // NET10収録アドレス情報

        // *ModeAddressの参照Index
        // NET10Controlよりあがってくる受信データは、必要なデータしか入っていないため、
        // このIndexにて受信データにアクセスを行う（本クラスで値を計算）
        public int SModeCheckCount = 0, OModeCheckCount = 0;

        //********************************************************************************
        // コンストラクタ
        //********************************************************************************
        public NET10Address()
        {
        }

        //********************************************************************************
        // デストラクタ
        //********************************************************************************
        ~NET10Address()
        {
        }

        //********************************************************************************
        // NET10収録アドレスをファイルから読み出す
        //********************************************************************************
        public bool LoadAddress()
        {
            int i, j;
            bool flag = false;

            try
            {
                //************************************************************
                // NET10アドレスが定義されているファイルがあるか確認
                // 1つでもファイルが無ければエラー
                foreach (string file in ADDRESS_FILENAME)
                {
                    if (file == "")     // DTMasterは無視
                        continue;

                    // ファイルがあるか確認
                    string path = Directory.GetCurrentDirectory() + ADDRESS_FOLDER + file;
                    if (File.Exists(path) == false)
                        return false;
                }

                //************************************************************
                // NET10収録アドレスの読込（区分毎）
                flag = true;

                var addrInfo = new NET10_AddressFileInfo[ADDRESS_FILENAME.Length];   // アドレス情報
                var addrCount = new int[ADDRESS_FILENAME.Length];    // 区分毎のアドレスブロック数

                for ( i = 0; i < ADDRESS_FILENAME.Length; i++)
                {
                    if ( i == (int)NET10_DIV.DTMaster)     // DTMasterは無視
                        continue;

                    // 初期化
                    string file = Directory.GetCurrentDirectory() + ADDRESS_FOLDER + ADDRESS_FILENAME[i];

                    TextFieldParser parser = new TextFieldParser(file, System.Text.Encoding.GetEncoding("Shift_JIS"));

                    addrInfo[i].SAddr = new List<short>();
                    addrInfo[i].EAddr = new List<short>();

                    // ファイルの読み出し+解析
                    using (parser)
                    {
                        parser.TextFieldType = FieldType.Delimited;
                        parser.SetDelimiters(","); // 区切り文字はコンマ

                        // 1行ずつ読み込みList<short>のテーブルにデータを格納する
                        while (!parser.EndOfData)
                        {
                            string[] row = parser.ReadFields();

                            // 開始、終了アドレス列に文字列が入っているもののみ読み込む
                            if (row.Count() >= 2)   
                            {
                                short start = ToInt16(row[0], 16);
                                short end = ToInt16(row[1], 16);

                                if ( ( row[0] != "" ) && (row[0] != "") )
                                {
                                    addrInfo[i].SAddr.Add(start);
                                    addrInfo[i].EAddr.Add(end);
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }

                        // 開始、終了アドレスの個数を格納
                        addrCount[i] = addrInfo[i].SAddr.Count();
                        if (addrCount[i] <= 0)  // アドレスが一個もなかったらNG
                        {
                            flag = false;
                            break;
                        }
                    }
                }

                if (flag == false)
                    return false;

                //************************************************************
                // NET10収録アドレスの格納

                // 開始・終了アドレス個数の計算（パラメータファイルの総行数）
                m_AddrInfo.BlockCount = 0;
                foreach (int count in addrCount)
                    m_AddrInfo.BlockCount += (short)count;

                // その他変数の確保
                m_AddrInfo.SAddr = new short[m_AddrInfo.BlockCount];
                m_AddrInfo.EAddr = new short[m_AddrInfo.BlockCount];
                m_AddrInfo.DType = new short[m_AddrInfo.BlockCount];
                m_AddrInfo.DCnt = new short[m_AddrInfo.BlockCount];
                m_AddrInfo.DelimitNo = new int[ADDRESS_FILENAME.Length, 2];

                // DTypeの設定（使用していない模様）
                for (i = 0; i < m_AddrInfo.BlockCount; i++)
                    m_AddrInfo.DType[i] = 24;

                // NET10収録アドレスの格納
                int blockCount = 0; // ブロック位置（SAddr等の配列Index）
                int dataCount = 0;  // データ位置（NET10からの受信バッファのIndex）

                for (i = 0; i < ADDRESS_FILENAME.Length; i++)
                {
                    if (i == (int)NET10_DIV.DTMaster)     // DTMasterは無視
                        continue;

                    m_AddrInfo.DelimitNo[i, 0] = dataCount;    // 区分の開始Index

                    for ( j = 0; j < addrCount[i]; j++ )
                    {
                        // 「W1950:浚渫状態　準備」のバッファ位置を保持
                        if (addrInfo[i].SAddr[j] ==  SMODE_ADDRESS)
                            SModeCheckCount = dataCount;
                        
                        // 開始、終了アドレス、開始、終了アドレス間のWORD数を格納
                        m_AddrInfo.SAddr[blockCount] = addrInfo[i].SAddr[j];
                        m_AddrInfo.EAddr[blockCount] = addrInfo[i].EAddr[j];
                        m_AddrInfo.DCnt[blockCount] = (short)(addrInfo[i].EAddr[j] - addrInfo[i].SAddr[j] + 1);

                        dataCount += m_AddrInfo.DCnt[blockCount];   // 受信WORD数の計算
                        blockCount++;

                        // 「W0302:推進モード自動油回収」のバッファ位置を保持
                        if (addrInfo[i].EAddr[j] == OMODE_ADDRESS)
                            OModeCheckCount = dataCount - 1;
                    }

                    m_AddrInfo.DelimitNo[i, 1] = dataCount - 1;    // 区分の終了Index
                }

                // 旧ソースのDivCount、DebTemp、aaaは使用箇所が無かったため計算せず
#if false
                Console.WriteLine( "Total address count : {0}", m_AddrInfo.BlockCount );
#endif
            }
            catch
            {
                return false;
            }

            return true;
        }
    }
}
