using System.Drawing;

namespace DJISortTool
{
    // 제품 정보: 프로그램과 설치·제거 프로그램이 함께 씀. 버전은 여기 한 곳만 고치면 됨 (build.ps1도 여기서 읽음)
    static class Product
    {
        public const string Name      = "DJI 타입 정리기";          // DJI가 만든 정식 프로그램이 아님을 분명히 (1.2까지 이름: DJI정리툴)
        public const string OldName   = "DJI정리툴";                // 옛 이름. 업데이트·제거할 때 옛 이름의 바로가기를 정리
        public const string Version   = "1.3";
        public const string Stage     = "";                         // 이름 옆 표시 (예: "베타"). 정식이면 ""
        public const string Id        = "DJISortTool";              // 실행 파일·설치 폴더·설정 폴더·우클릭 메뉴 이름
        public const string Exe       = Id + ".exe";
        public const string Summary   = "DJI 카메라·드론 영상을 촬영일별 폴더로 정리합니다";
        public const string Publisher = "이중필름";
        public const string Channel   = "https://www.youtube.com/@2J_FILM";
        public const string Contact   = "nasunfilm@gmail.com";
        public const string Home      = "https://github.com/musiqsb-svg/dji-sort-tool";
        public const string MenuText  = "DJI 타입 정리기로 정리";
        public const string Unofficial = "DJI 정식 프로그램이 아닙니다";       // 머리말·설치 화면의 짧은 안내
        public const string Notice    = "DJI가 만든 정식 프로그램이 아닌, 개인이 만든 비공식 프로그램입니다. DJI는 SZ DJI Technology Co., Ltd.의 상표입니다.";

        // [설정 › 앱]의 제거 항목 키. 1.2까지 Inno Setup이 쓰던 이름 그대로 (옛 설치본을 찾고, 옛 제거 파일로도 지울 수 있게)
        public const string UninstallKey = "{38358392-BC3B-47AA-8BA8-E612A69A7E7D}_is1";

        public static readonly Color Accent     = Color.FromArgb(37, 99, 235);    // 파랑
        public static readonly Color AccentDeep = Color.FromArgb(29, 78, 216);
        public static readonly Color AccentSoft = Color.FromArgb(235, 243, 255);

        // 이름 뒤 조사 (이름을 바꿔도 맞게): 을/를 · 이/가 · 과/와 · 으로/로
        public static string Eul { get { return Batchim() ? "을" : "를"; } }
        public static string Ga  { get { return Batchim() ? "이" : "가"; } }
        public static string Gwa { get { return Batchim() ? "과" : "와"; } }
        public static string Ro  { get { return Batchim() && Jong() != 8 ? "으로" : "로"; } }   // ㄹ받침은 '로'

        static int Jong() { char c = Name[Name.Length - 1]; return c >= 0xAC00 && c <= 0xD7A3 ? (c - 0xAC00) % 28 : 0; }
        static bool Batchim() { return Jong() != 0; }
    }
}
