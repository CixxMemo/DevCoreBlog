namespace DevCoreBlog.Services.Publishing;

/// <summary>Explicit editor intent; Save keeps the stored publication settings.</summary>
public enum PostSaveAction { SaveDraft, Schedule, Publish, Save }
