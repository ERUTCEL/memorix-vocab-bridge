// VocabServerData.cs
// 백엔드 서버(localhost:8000)와 주고받는 데이터 구조 정의
// JsonUtility 호환을 위해 필드명은 서버 JSON과 동일한 snake_case 사용

using System;
using System.Collections.Generic;

// ── 서버 응답 구조 ─────────────────────────────────────────────

[Serializable]
public class HealthResponse
{
    public string status;
    public bool ai3_ready;
    public bool words_ready;
    public bool user_ready;
    public bool schedule_ready;
}

[Serializable]
public class RatedWordFsrs
{
    public float stability;
    public float difficulty;
    public string due_date;
    public int review_count;
    public string state;
}

[Serializable]
public class RatedWord
{
    public string word;
    public string pos;
    public string meaning;
    public int rating;
    public string source;       // "oxford_db" | "predicted" | "api_verified"
    public float confidence;
    public bool learned;
    public RatedWordFsrs fsrs;
}

[Serializable]
public class RatedWordListStats
{
    public float mean_rating;
    public float median_rating;
    public float std_rating;
}

[Serializable]
public class RatedWordList
{
    public string generated_at;
    public int total_words;
    public int oxford_matched;
    public int predicted;
    public int api_verified;
    public RatedWordListStats stats;
    public List<RatedWord> words;
}

// ── 스케줄 ────────────────────────────────────────────────────

[Serializable]
public class ScheduleWord
{
    public string word;
    public int rating;
    public string type;         // "new" | "review" | "supplement"
    public string fsrs_due;     // review 단어만 존재
}

[Serializable]
public class ScheduleStats
{
    public int new_count;
    public int review_count;
    public int supplement_count;
}

[Serializable]
public class DailySchedule
{
    public string date;
    public int user_rating;
    public int total_words;
    public List<ScheduleWord> new_words;
    public List<ScheduleWord> review_words;
    public List<ScheduleWord> db_supplement;
    public ScheduleStats stats;
}

// ── 유저 프로필 ───────────────────────────────────────────────

[Serializable]
public class UserProfile
{
    public string user_id;
    public int user_rating;
    public List<int> rating_history;
    public int k_factor;
    public int total_sessions;
    public bool onboarding_completed;
    public string created_at;
    public string last_updated;
}

// ── 온보딩 퀴즈 ───────────────────────────────────────────────

[Serializable]
public class OnboardingQuestion
{
    public int order;
    public string word;
    public int rating;
    public string bucket;       // "A1" | "A2" | "B1" | "B2" | "C1"
}

[Serializable]
public class OnboardingQuiz
{
    public int total_questions;
    public List<OnboardingQuestion> questions;
}

// ── 요청 구조 (POST body) ─────────────────────────────────────

[Serializable]
public class QuizAnswer
{
    public int order;
    public string word;
    public bool correct;
    public int response_time_ms;
}

[Serializable]
public class OnboardingSubmitRequest
{
    public List<QuizAnswer> answers;
}

[Serializable]
public class SessionAnswer
{
    public string word;
    public bool correct;
    public int rating_given;    // 1=Again, 2=Hard, 3=Good, 4=Easy
}

[Serializable]
public class SessionResultRequest
{
    public List<SessionAnswer> answers;
}

// ── CSV 업로드 응답 ───────────────────────────────────────────

[Serializable]
public class UploadCsvResponse
{
    public string status;
    public int total_words;
    public int oxford_matched;
    public int predicted;
    public int api_verified;
}
