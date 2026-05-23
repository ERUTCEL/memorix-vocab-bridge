# memorix-vocab-bridge

Memorix Unity 게임 ↔ Vocab Rating System(Python 백엔드) 연동 스크립트 패키지

---

## 파일 구성

```
Scripts/
├── VocabServerData.cs      # 서버 JSON ↔ C# 데이터 클래스 정의
├── ApiClient.cs            # UnityWebRequest 기반 HTTP 유틸리티
└── VocabServerManager.cs   # 메인 통합 매니저 (MonoBehaviour)
```

---

## 통합 전략

| 역할 | 담당 |
|------|------|
| FSRS 스케줄링 (카드 복습 간격) | **Unity 게임 기존 코드** (MainScheduler, FSRSScheduler) |
| 단어 난이도 레이팅 (IRT) | **Python 백엔드** (Oxford DB + KNN + Claude API) |
| 유저 실력 추정 | **Python 백엔드** (온보딩 퀴즈 → IRT sigmoid) |
| 세션 결과 동기화 | **Python 백엔드** (Elo 방식 userRating 업데이트) |

> 게임의 기존 FSRS 코드(FSRSScheduler, MainScheduler, StudyManager 등)는 **수정하지 않습니다.**

---

## 설치 방법

1. `Scripts/` 폴더 안의 3개 파일을 Unity 프로젝트의 `Assets/00_Scripts/Network/` 폴더에 복사
2. 씬의 빈 GameObject에 `VocabServerManager` 컴포넌트 추가
3. Python 백엔드 서버가 `localhost:8000`에서 실행 중이어야 함

---

## 사용 예시

### 1. 서버에서 덱 생성 (기존 CSVLoader 대체)

```csharp
// 기존 방식 (CSV 로컬 파일)
// DeckSystem.CreateDeckFromCSV(csvPath, "내 단어장");

// 새 방식 (서버에서 레이팅된 단어 로드)
StartCoroutine(VocabServerManager.Instance.CreateDeckFromServer(
    deckName: "내 단어장",
    onSuccess: deck =>
    {
        string deckId = deck.id;
        // 이후 기존 StudyManager.Load(deckId) 그대로 사용 가능
    }
));
```

### 2. 오늘의 스케줄 적용 (IRT 기반 단어 우선순위)

```csharp
// StudyManager.StartToday() 호출 전에 실행
StartCoroutine(VocabServerManager.Instance.ApplyTodaySchedule(
    deck: MANAGER.StudyManager.deck,
    dailyLimit: 100,
    onSuccess: schedule =>
    {
        Debug.Log($"신규 {schedule.stats.new_count}개, 복습 {schedule.stats.review_count}개");
        // 이후 MANAGER.StudyManager.StartToday() 호출
    }
));
```

### 3. 세션 결과 제출 (userRating 업데이트)

```csharp
// LearnManager.RateCard() 또는 QuizManager.ReviewCards() 완료 후 호출

var answers = new List<SessionAnswer>();
foreach (var log in quizLogs)
{
    answers.Add(new SessionAnswer
    {
        word = log.card.front,
        correct = log.isCorrect,
        rating_given = GetRating(log.card, log.isCorrect) // 기존 GetRating() 재사용
    });
}

StartCoroutine(VocabServerManager.Instance.SubmitSessionResult(
    answers: answers,
    onSuccess: profile =>
    {
        Debug.Log($"새 유저 레이팅: {profile.user_rating}");
    }
));
```

### 4. 단어 레이팅 조회 (퀴즈 난이도 표시 등)

```csharp
int rating = VocabServerManager.Instance.GetWordRating("negotiate");
// rating: 568 (Oxford DB 레이팅), -1이면 미등록 단어
```

### 5. 온보딩 퀴즈 (최초 1회)

```csharp
// 퀴즈 문제 가져오기
StartCoroutine(VocabServerManager.Instance.GetOnboardingQuiz(quiz =>
{
    foreach (var q in quiz.questions)
        Debug.Log($"[{q.bucket}] {q.word} (rating={q.rating})");
}));

// 퀴즈 결과 제출
var answers = new List<QuizAnswer>
{
    new QuizAnswer { order = 1, word = "negotiate", correct = true, response_time_ms = 2300 },
    new QuizAnswer { order = 2, word = "abolish",   correct = false, response_time_ms = 4500 },
};

StartCoroutine(VocabServerManager.Instance.SubmitOnboardingAnswers(
    answers: answers,
    onSuccess: profile =>
    {
        Debug.Log($"온보딩 완료. userRating={profile.user_rating}");
    }
));
```

---

## 서버 엔드포인트 요약

| Method | Endpoint | 기능 |
|--------|----------|------|
| GET | `/api/health` | 서버 상태 확인 |
| POST | `/api/upload-csv` | 유저 CSV 업로드 → AI2 레이팅 |
| GET | `/api/words/all` | 전체 단어 + 레이팅 반환 |
| GET | `/api/schedule/today` | 오늘 학습 스케줄 |
| POST | `/api/session/result` | 세션 결과 제출 |
| GET | `/api/onboarding/quiz` | 온보딩 퀴즈 문제 |
| POST | `/api/onboarding/submit` | 온보딩 답안 제출 |
| GET | `/api/user/profile` | 유저 프로필 |

---

## 의존 관계

- **Python 백엔드**: `cap/` 디렉토리의 FastAPI 서버 (`uvicorn unity_bridge.server:app`)
- **Unity 기존 코드**: `Card`, `Deck`, `SaveSystem`, `CustomTime`, `MainScheduler`, `FSRSScheduler` 클래스 필요
