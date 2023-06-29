using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using static System.Convert;

namespace _01_NET10収録_NET10toFILE_
{
    //********************************************************************************
    // ID情報の管理クラス
    // DTMasterの浚渫ID、油回収IDのファイルI/Oを行う
    //********************************************************************************
    public class IDManager
    {
        // ID情報の保存ファイル名
        private string IDFILENAME = "Net102FileIDInfo.txt";

        // 浚渫ID
        public int m_DredgeID { get; set; } = 0;

        // 油回収ID
        public int m_OilID { get; set; } = 0;

        //********************************************************************************
        // コンストラクタ
        //********************************************************************************
        public IDManager()
        {
        }

        //********************************************************************************
        // 読み込み
        //********************************************************************************
        public bool loadFile()
        {
            int value;
            string path = Directory.GetCurrentDirectory() + "\\" + IDFILENAME;

            try
            {
                // ID情報の読み込み
                using (StreamReader file = new StreamReader(path, Encoding.GetEncoding("shift_jis")))
                {
                    // 浚渫ID
                    if (int.TryParse(file.ReadLine(), out value) == false)
                        throw new Exception();
                    m_DredgeID = value;

                    // 油回収ID
                    if (int.TryParse(file.ReadLine(), out value) == false)
                        throw new Exception();
                    m_OilID = value;
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        //********************************************************************************
        // 保存
        //********************************************************************************
        public bool saveFile( int dredgeID, int oilID )
        {
            string path = Directory.GetCurrentDirectory() + "\\" + IDFILENAME;

            try
            {
                // ID情報の保存
                using (StreamWriter file = new StreamWriter(path, false, Encoding.GetEncoding("shift_jis")))
                {
                    file.WriteLine(m_DredgeID.ToString());
                    file.WriteLine(m_OilID.ToString());
                }
            }
            catch
            {
                return false;
            }

            return true;
        }
    }
}
