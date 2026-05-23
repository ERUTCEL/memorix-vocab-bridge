// VocabServerManager.cs
// 백엔드 서버와 Memorix 게임을 연결하는 메인 통합 매니저
//
// [사용법]
// 1. 이 스크립트를 씬의 빈 GameObject에 추가
// 2. 게임 시작 시 VocabServerManager.Instance 로 접근
//
// [통합 전략]
// - 게임의 기존 FSRS(MainScheduler/FSRSScheduler)는 그대로 유지
// - 백엔드 역할: 단어 난이도 레이팅(IRT) 제공 + 유저 실력 추정 + 세션 결과 동기화
// - 단어 레이팅은 Dictionary<word, rating>으로 캐싱해 퀴즈/학습 씬에서 참조 가능

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public class VocabServerManager : MonoBehaviour
{
    public static VocabServerManager Instance { get; private set; }

    // 서버에서 받아온 단어 레이팅 캐시 (word → IRT rating)
    // 다른 스크립트에서 GetWordRating(word) 로 조회
    private readonly Dictionary<string, int> _wordRatings = new();

    // 마지막으로 받아온 유저 프로필
    public UserProfile CurrentUserProfile { get; private set; }

    // 서버 연결 상태
    public bool IsServerReady { get; private set; } = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        // 게임 시작 시 서버 상태 체크
        StartCoroutine(CheckServerOnStart());
    }

    // ── 서버 상태 확인 ────────────────────────────────────────────

    private IEnumerator CheckServerOnStart()
    {
        yield return ApiClient.Ping(ok =>
        {
            IsServerReady = ok;
            if (ok)
                Debug.Log("[VocabServer] 서버 연결 성공");
            else
                Debug.LogWarning("[VocabServer] 서버 연결 실패. localhost:8000 확인 필요.");
        });

        if (IsServerReady)
            yield return RefreshUserProfile();
    }

    public IEnumerator CheckHealth(Action<HealthResponse> onSuccess, Action<string> onError = null)
    {
        yield return ApiClient.Get<HealthResponse>("/api/health", onSuccess, onError);
    }

    // ── 유저 프로필 ────────────────────────────────────────────────

    public IEnumerator RefreshUserProfile(Action<UserProfile> onSuccess = null)
    {
        yield return ApiClient.Get<UserProfile>("/api/user/profile", profile =>
        {
            CurrentUserProfile = profile;
            Debug.Log($"[VocabServer] userRating={profile.user_rating}, sessions={profile.total_sessions}");
            onSuccess?.Invoke(profile);
        });
    }

    // ── CSV 업로드 → AI2 실행 ────────────────────────────────────

    /// <summary>
    /// 유저 단어 CSV를 서버에 업로드하고 레이팅 예측을 실행합니다.
    /// 완료 후 단어 레이팅이 캐시에 로드됩니다.
    /// </summary>
    public IEnumerator UploadCsv(
        string csvLocalPath,
        Action<UploadCsvResponse> onSuccess = null,
        Action<string> onError = null)
    {
        if (!File.Exists(csvLocalPath))
        {
            string err = $"CSV 파일 없음: {csvLocalPath}";
            Debug.LogWarning(err);
            onError?.Invoke(err);
            yield break;
        }

        byte[] bytes = File.ReadAllBytes(csvLocalPath);
        string fileName = Path.GetFileName(csvLocalPath);

        yield return ApiClient.PostFile<UploadCsvResponse>(
            "/api/upload-csv",
            bytes,
            fileName,
            response =>
            {
                Debug.Log($"[VocabServer] CSV 업로드 완료: {response.total_words}단어");
                onSuccess?.Invoke(response);
            },
            onError);

        // 업로드 성공 후 레이팅 캐시 갱신
        yield return LoadWordRatings();
    }

    // ── 단어 레이팅 캐시 로드 ─────────────────────────────────────

    /// <summary>
    /// 서버의 rated_words.json을 받아 _wordRatings 캐시를 채웁니다.
    /// UploadCsv 또는 앱 시작 시 자동 호출됩니다.
    /// </summary>
    public IEnumerator LoadWordRatings(Action onDone = null)
    {
        yield return ApiClient.Get<RatedWordList>("/api/words/all", list =>
        {
            _wordRatings.Clear();
            foreach (var w in list.words)
                _wordRatings[w.word.ToLower()] = w.rating;

            Debug.Log($"[VocabServer] 레이팅 캐시 로드: {_wordRatings.Count}단어");
            onDone?.Invoke();
        });
    }

    /// <summary>
    /// 단어의 IRT 레이팅 반환. 미등록 단어는 -1 반환.
    /// </summary>
    public int GetWordRating(string word)
    {
        return _wordRatings.TryGetValue(word.ToLower(), out int r) ? r : -1;
    }

    // ── 덱 생성 (서버 기반 단어 + 게임 자체 FSRS) ─────────────────

    /// <summary>
    /// 서버에서 rated words를 받아 Memorix Deck을 생성합니다.
    /// 게임의 기존 CSVLoader 대신 이 메서드로 덱을 생성하면
    /// 각 Card에 서버 레이팅 기반의 단어들이 들어갑니다.
    /// FSRS 상태는 게임의 MainScheduler가 관리합니다.
    /// </summary>
    public IEnumerator CreateDeckFromServer(
        string deckName,
        Action<Deck> onSuccess,
        Action<string> onError = null)
    {
        RatedWordList wordList = null;

        yield return ApiClient.Get<RatedWordList>("/api/words/all", list =>
        {
            wordList = list;
        }, onError);

        if (wordList == null || wordList.words == null || wordList.words.Count == 0)
        {
            onError?.Invoke("서버에서 단어 목록을 가져오지 못했습니다.");
            yield break;
        }

        // 레이팅 캐시도 함께 갱신
        _wordRatings.Clear();
        foreach (var w in wordList.words)
            _wordRatings[w.word.ToLower()] = w.rating;

        // Deck 생성 (게임 기존 구조 그대로)
        string deckId = Guid.NewGuid().ToString();
        var deck = new Deck
        {
            id = deckId,
            name = deckName,
            startDate = CustomTime.GetTimeNow(),
        };

        // Card 생성: front=영어단어, back=한국어뜻
        deck.cards = new List<Card>();
        int cardId = 0;
        foreach (var w in wordList.words)
        {
            deck.cards.Add(new Card
            {
                id = cardId++,
                front = w.word,
                back = string.IsNullOrEmpty(w.meaning) ? "" : w.meaning,
            });
        }

        SaveSystem.SaveDeck(deck);

        Debug.Log($"[VocabServer] 덱 생성: '{deckName}' ({deck.cards.Count}단어)");
        onSuccess?.Invoke(deck);
    }

    // ── 오늘의 학습 스케줄 적용 ───────────────────────────────────

    /// <summary>
    /// 서버에서 오늘의 추천 단어 순서를 받아 deck.todayCardIds를 갱신합니다.
    /// 서버의 IRT(userRating ± 범위) 기반 우선순위 정렬이 적용됩니다.
    /// 연결 실패 시 기존 MainScheduler 방식으로 폴백됩니다.
    /// </summary>
    public IEnumerator ApplyTodaySchedule(
        Deck deck,
        int dailyLimit = 100,
        Action<DailySchedule> onSuccess = null)
    {
        DailySchedule schedule = null;

        yield return ApiClient.Get<DailySchedule>(
            $"/api/schedule/today?daily_limit={dailyLimit}",
            s => schedule = s);

        if (schedule == null)
        {
            Debug.LogWarning("[VocabServer] 스케줄 로드 실패. MainScheduler 폴백 사용.");
            yield break;
        }

        // 서버 스케줄 순서(new → review → supplement) 기반으로 todayCardIds 재구성
        var orderedWords = new List<string>();
        if (schedule.new_words != null)     orderedWords.AddRange(schedule.new_words.Select(w => w.word));
        if (schedule.review_words != null)  orderedWords.AddRange(schedule.review_words.Select(w => w.word));
        if (schedule.db_supplement != null) orderedWords.AddRange(schedule.db_supplement.Select(w => w.word));

        // word → Card 매핑
        var wordToCard = deck.cards.ToDictionary(c => c.front.ToLower(), c => c);

        deck.todayCardIds.Clear();
        foreach (var word in orderedWords)
        {
            if (wordToCard.TryGetValue(word.ToLower(), out var card))
                deck.todayCardIds.Add(card.id);
        }

        // 혹시 누락된 카드 (서버 스케줄에 없는 덱 내 카드)는 뒤에 추가
        var scheduledIds = new HashSet<int>(deck.todayCardIds);
        foreach (var card in deck.cards)
        {
            if (!scheduledIds.Contains(card.id))
                deck.todayCardIds.Add(card.id);
        }

        Debug.Log($"[VocabServer] 스케줄 적용: 신규={schedule.stats?.new_count}, 복습={schedule.stats?.review_count}");
        onSuccess?.Invoke(schedule);
    }

    // ── 세션 결과 제출 → userRating 업데이트 ─────────────────────

    /// <summary>
    /// 학습/퀴즈 세션 결과를 서버에 전송합니다.
    /// 서버는 IRT Elo 공식으로 userRating을 업데이트하고
    /// 새 userRating과 k_factor를 반환합니다.
    ///
    /// [LearnManager 연동 예시]
    ///   var answers = new List<SessionAnswer>();
    ///   answers.Add(new SessionAnswer { word = card.front, correct = isCorrect, rating_given = rating });
    ///   StartCoroutine(VocabServerManager.Instance.SubmitSessionResult(answers));
    /// </summary>
    public IEnumerator SubmitSessionResult(
        List<SessionAnswer> answers,
        Action<UserProfile> onSuccess = null,
        Action<string> onError = null)
    {
        if (answers == null || answers.Count == 0) yield break;

        var body = new SessionResultRequest { answers = answers };

        yield return ApiClient.Post<UserProfile>("/api/session/result", body, profile =>
        {
            CurrentUserProfile = profile;
            Debug.Log($"[VocabServer] 세션 완료. 새 userRating={profile.user_rating}");
            onSuccess?.Invoke(profile);
        }, onError);
    }

    // ── 온보딩 퀴즈 ───────────────────────────────────────────────

    /// <summary>
    /// 서버에서 온보딩 퀴즈 문제를 가져옵니다.
    /// CEFR 5구간(A1~C1) × 20문제 = 100문제 반환.
    /// </summary>
    public IEnumerator GetOnboardingQuiz(
        Action<OnboardingQuiz> onSuccess,
        Action<string> onError = null)
    {
        yield return ApiClient.Get<OnboardingQuiz>("/api/onboarding/quiz", onSuccess, onError);
    }

    /// <summary>
    /// 온보딩 퀴즈 결과를 제출합니다.
    /// 서버가 IRT sigmoid로 userRating 초기값을 계산해 저장합니다.
    /// </summary>
    public IEnumerator SubmitOnboardingAnswers(
        List<QuizAnswer> answers,
        Action<UserProfile> onSuccess = null,
        Action<string> onError = null)
    {
        var body = new OnboardingSubmitRequest { answers = answers };

        yield return ApiClient.Post<UserProfile>("/api/onboarding/submit", body, profile =>
        {
            CurrentUserProfile = profile;
            Debug.Log($"[VocabServer] 온보딩 완료. userRating={profile.user_rating}");
            onSuccess?.Invoke(profile);
        }, onError);
    }
}
