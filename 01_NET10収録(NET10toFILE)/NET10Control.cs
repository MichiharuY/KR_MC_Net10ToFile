using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace _01_NET10収録_NET10toFILE_
{
    // NET10データ取得イベント
    public delegate void ArrivedData(short[] rcvBuf);

    //********************************************************************************
    // MELSECNET/Hの通信モジュール
    //********************************************************************************
    public class NET10Control
    {
        // 定数
        const int TIMER_INTERVAL = 50;  // タイマー間隔

        // MELSECNET/H関連の定数
        const short CHANNEL = 51;       // 通信回線のチャネルNo.(MELSECNET/H（1 枚目）)
        const short MODE = -1;          // ダミー（-1固定）
        const int NETNO = 0x00;         // ネットワークNo.（自局）
        const int STNO = 0xFF;          // 局番（自局）
        const int DEVTP = 24;           // デバイスタイプ（リンクレジスタ(LW)）
        const int DEVNO = 0;            // 先頭デバイスNo.
        const int RCVSIZE = 0x4000;     // 読み出しバイトサイズ（16384 WORD）

        // 変数
        private bool m_Open;            // true : 通信中

        private int m_ChPath;           // オープンされた回線のパスのポインタ

        private short m_BlockCount;     // m_StartAddrの配列数
        private short[] m_StartAddr;    // NET10の収録開始アドレス（複数）
        private short[] m_RcvCount;     // 各開始アドレスのデータ点数(開始から終了アドレスまでのWORD数)

        private short[] m_AllRcvBuff;   // 受信バッファ（全データ受信用）
        private short[] m_RcvBuff;      // 受信バッファ（指定されたアドレスのデータのみ格納）

        int m_OldRcvTime = 0;           // 最後に受信した時刻（秒のみ）

        // NET10の受信用タイマー
        private Timer m_Timer;

        // NET10データ取得イベント
        public event ArrivedData OnArrivedData;

        //********************************************************************************
        // コンストラクタ
        //********************************************************************************
        public NET10Control()
        {
            m_Open = false;

            // NET10の受信タイマーの初期化
            m_Timer = new Timer();
            m_Timer.Elapsed += new ElapsedEventHandler(RcvTimerFire);
            m_Timer.Interval = TIMER_INTERVAL;
            m_Timer.AutoReset = true;

            m_OldRcvTime = DateTime.Now.Second;
        }

        //********************************************************************************
        // デストラクタ
        //********************************************************************************
        ~NET10Control()
        {
            // 通信停止
            EndComm();
        }

        //********************************************************************************
        // NET10の受信アドレスの設定
        //********************************************************************************
        public bool SetAddress(short[] startAddr, short[] addrCount, short blockCount )
        {
            short total = 0;

            m_BlockCount = blockCount;
            m_StartAddr = startAddr;
            m_RcvCount = addrCount;

            // 受信データの総WORD数の計算
            for (int i = 0; i < m_BlockCount; i++)
                total += m_RcvCount[i];

            // 受信バッファの確保
            m_RcvBuff = new short[total];
            m_AllRcvBuff = new short[RCVSIZE];

            return true;
        }

        //********************************************************************************
        // NET10の通信開始
        //********************************************************************************
        public bool StartComm()
        {
            if (m_Open == true)
                return true;
            
            // 通信回線のオープン
            short ret = MDFUNC32.mdOpen( CHANNEL, MODE, ref m_ChPath );
            if ( (ret != 0) && (ret != 66))    // OPEN 済みエラーも正常終了とみなす
                return false;

            m_Open = true;

            // 受信タイマーの開始
            m_Timer.Start();

            return true;
        }

        //********************************************************************************
        // NET10の通信停止
        //********************************************************************************
        public bool EndComm()
        {
            if (m_Open == false)
                return true;

            // 受信タイマーの停止
            m_Timer.Stop();

            // 通信回線のクローズ
            short ret = MDFUNC32.mdClose(m_ChPath);
            if (ret != 0)
                return false;

            m_Open = false;

            return true;
        }

        //********************************************************************************
        // NET10からデータを受信する（タイマー処理）
        //********************************************************************************
        private void RcvTimerFire(object sender, ElapsedEventArgs e)
        {
            if (m_Open == false)
                return;
            
            try
            {
                // Winodws時刻の秒が変化しないと受信を行わない
                if ( DateTime.Now.Second == m_OldRcvTime )
                    return;
                m_OldRcvTime = DateTime.Now.Second;

                m_Timer.Stop();

                int size = RCVSIZE * 2;   // 受信データサイズ（WORDをBYTEにするため2倍）

                // NET10よりデータを取得する(全データ取得)
                int ret = MDFUNC32.mdReceiveEx(m_ChPath, NETNO, STNO, DEVTP, DEVNO, ref size, ref m_AllRcvBuff[0]);
                if (ret != 0)
                {
                    Console.WriteLine("mdReceive error [{0}]", ret);
                    return;
                }

                // 取得データから必要なデータ（SetAddressで要求されたもの）をコピーする
                int i = 0, j = 0, offset = 0;

                for ( i = 0; i < m_BlockCount; i++ )
                {
                    for ( j = 0; j < m_RcvCount[i]; j++ )
                    {
                        m_RcvBuff[offset] = m_AllRcvBuff[m_StartAddr[i] + j];
                        offset++;
                    }
                }

                // 受信イベント
                OnArrivedData(m_RcvBuff);
            }
            catch( Exception exp )
            {
                Debug.WriteLine(exp.Message);
            }
            finally
            {
                if( m_Open == true )
                    m_Timer.Start();
            }

            return;
        }
    }
}
