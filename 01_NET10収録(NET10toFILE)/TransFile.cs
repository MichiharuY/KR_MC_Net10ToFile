using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.IO;

namespace _01_NET10収録_NET10toFILE_
{
    // ファイル転送の動作状態
    enum TRANS_STATE
    {
        フォルダ確認 = 0,
        転送中
    }

    // ファイル転送エラーイベント
    public delegate void FileTransError(string error);

    // ファイル転送完了イベント
    public delegate void FileTransComplete(int count);

    //********************************************************************************
    // NET10収録ファイル転送クラス
    //********************************************************************************
    public class TransFile
    {
        // 定数
        public const int TIMER_INTERVAL = 1;            // 転送開始タイマー間隔

        // 収録ファイルのファイル拡張子
        public const string REC_FILE_EXT = ".txt";      // 収録中ファイル
        public const string TRANS_FILE_EXT = ".bul";    // 収録完了ファイル
        public const string ERROR_FILE_EXT = ".x";      // 収録データにエラーがあったファイル

        // 変数
        public string[] m_SrcDir, m_DstDir;             // 転送元、転送先フォルダパス

        public bool isTransFile;                        // true : 転送処理中
        public bool haltTransFile { get; set; }         // true : 転送処理中断（未使用）

        private Timer m_Timer;                          // 転送開始タイマー

        // ファイル転送エラーイベント
        public event FileTransError OnFileTransError;

        // ファイル転送完了イベント
        public event FileTransComplete OnFileTransComplete;

        //********************************************************************************
        // コンストラクタ
        //********************************************************************************
        public TransFile( int pathCount )
        {
            // フォルダパス配列の確保
            m_SrcDir = new string[pathCount];
            m_DstDir = new string[pathCount];

            // NET10の受信タイマーの初期化
            m_Timer = new Timer();
            m_Timer.Elapsed += new ElapsedEventHandler(TransTimerFire);
            m_Timer.Interval = TIMER_INTERVAL;
            m_Timer.AutoReset = false;  // 1回のみ

            haltTransFile = false;
        }

        //********************************************************************************
        // コンストラクタ
        //********************************************************************************
        ~TransFile()
        {
            haltTransFile = true;

            if ( m_Timer != null )
            {
                m_Timer.Stop();
            }
        }

        //********************************************************************************
        // ファイル転送開始指示
        //********************************************************************************
        public bool startFileTrans()
        {
            if(isTransFile == true)
            {
                // ファイル転送中の場合は終了
                return false;
            }
            else
            {
                // ファイル転送処理の開始
                haltTransFile = false;
                m_Timer.Start();
                return true;
            }
        }

        //********************************************************************************
        // ファイル転送処理
        //********************************************************************************
        private void TransTimerFire(object sender, ElapsedEventArgs e)
        {
            bool flag = false;
            TRANS_STATE state = 0;

            // 転送中止確認
            if (haltTransFile == true)
                return;
            
            try
            {
                //************************************************************
                // 転送先フォルダ確認
                state = TRANS_STATE.フォルダ確認;
                foreach (string dir in m_DstDir)
                {
                    flag = Directory.Exists(dir);
                    if ( flag == false )
                    {
                        OnFileTransError("TransFile:転送先フォルダが見つかりませんでした。 : " + dir);
                        isTransFile = false;
                        return;
                    }
                }

                //************************************************************
                // ファイル転送開始
                int totalCount = 0;

                state = TRANS_STATE.転送中;

                isTransFile = true;
                
                for (int i = 0; i <= (int)NET10_DIV.Net10Data; i++)
                {
                    string[] files;
                
                    // 収録データ保存済みファイルの検索（拡張子が.bulのファイル）
                    files = Directory.GetFiles(m_SrcDir[i], "*" + TRANS_FILE_EXT, SearchOption.AllDirectories);

                    totalCount = 0;
                        
                    // 見つかったファイルを転送(ファイル移動)
                    foreach ( string path in files )
                    {
                        string fileName = Path.GetFileName(path);
                        
                        totalCount += File.ReadAllLines(path).Count();  // 転送ファイルの行数をカウントする

                        File.Move(path, m_DstDir[i] + "\\" + fileName);
                    }
                }

                // 受信完了イベント
                OnFileTransComplete(totalCount);

                isTransFile = false;
            }
            catch( Exception exp )
            {
                // 例外が発生した場合はイベントにて通知
                if ( state == TRANS_STATE.フォルダ確認)
                {
                    OnFileTransError("TransFile:転送先フォルダが見つかりませんでした。");
                }
                else
                {
                    OnFileTransError("TransFile:" + exp.Message );
                }

                isTransFile = false;
            }

            return;
        }
    }
}
