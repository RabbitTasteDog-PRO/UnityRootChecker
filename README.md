# UnityRootChecker
루팅사용자 체크를 위한 스크립트 입니다


## 사용법
// 데이터 파싱 전 Root Check
// 루팅 유저를 거르는 용도로 완벽하지 않음 
var checker = new RootCheckerManager();
bool restrict = checker.ShouldRestrictSensitiveFeatures(out string reason);
if (restrict == true)
 {
    Debug.LogError($"[Security] Restrict sensitive features. reason={reason}");
   // 결제/랭킹/거래 등만 제한
 }

 Debug.Log($"루팅 사용자 체크 : <color=yellow>{checker.GetData()}</color>");
