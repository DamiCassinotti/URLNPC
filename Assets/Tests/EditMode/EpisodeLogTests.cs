using NUnit.Framework;

// Episode bookkeeping as pure state: which index events are stamped with,
// where an episode's stats start and stop, and the two ways a new episode
// opens — a decided round (training restarts in place) and a scene reload
// (human play). Getting the overlap between those two wrong is what makes
// summaries go missing or double-count.
public class EpisodeLogTests
{
    static EpisodeLog Started(float now = 0f)
    {
        var log = new EpisodeLog();
        log.Begin(now);
        return log;
    }

    [Test]
    public void Episodes_AreNumberedFromOne()
    {
        var log = new EpisodeLog();
        Assert.That(log.Index, Is.Zero, "nothing is logged before the first episode opens");

        log.Begin(0f);
        Assert.That(log.Index, Is.EqualTo(1));
    }

    [Test]
    public void Elapsed_MeasuresFromTheEpisodeStart()
    {
        EpisodeLog log = Started(10f);
        Assert.That(log.Elapsed(13.5f), Is.EqualTo(3.5f));
    }

    [Test]
    public void Shots_AndDamage_AccumulatePerSide()
    {
        EpisodeLog log = Started();
        log.RecordShot("NPC", hit: true, damage: 50f);
        log.RecordShot("NPC", hit: false, damage: 0f);
        log.RecordDamage("Player", 50f);

        string json = log.SidesJson();
        Assert.That(json, Does.Contain("\"NPC\":{\"damageDealt\":50,\"damageTaken\":0,\"shots\":2,\"hits\":1,\"accuracy\":0.5}"));
        Assert.That(json, Does.Contain("\"Player\":{\"damageDealt\":0,\"damageTaken\":50,\"shots\":0,\"hits\":0,\"accuracy\":0}"));
    }

    [Test]
    public void Accuracy_IsZeroWithoutShots()
    {
        EpisodeLog log = Started();
        log.RecordDamage("Player", 10f);

        Assert.That(log.SidesJson(), Does.Contain("\"accuracy\":0"));
    }

    [Test]
    public void SidesJson_IsEmptyBeforeAnythingHappens()
    {
        Assert.That(Started().SidesJson(), Is.EqualTo("\"sides\":{}"));
    }

    [Test]
    public void EndRound_OpensTheNextEpisodeWithCleanStats()
    {
        EpisodeLog log = Started();
        log.RecordShot("NPC", hit: true, damage: 50f);

        log.EndRound(20f);

        Assert.That(log.Index, Is.EqualTo(2));
        Assert.That(log.StartTime, Is.EqualTo(20f));
        Assert.That(log.SidesJson(), Is.EqualTo("\"sides\":{}"), "stats must not leak into the next round");
    }

    [Test]
    public void BackToBackRoundEnds_EachOpenAnEpisode()
    {
        // Training: no scene reload ever comes, the round restarts in place.
        EpisodeLog log = Started();
        log.EndRound(10f);
        log.EndRound(20f);
        log.EndRound(30f);

        Assert.That(log.Index, Is.EqualTo(4));
    }

    [Test]
    public void SceneLoad_AfterARoundEnd_DoesNotBurnAnIndex()
    {
        // Human play: the kill ends the round, then the end-of-round button
        // reloads the scene. Both must land on one episode, not two.
        EpisodeLog log = Started();
        log.EndRound(10f);

        log.SceneLoaded(12f);

        Assert.That(log.Index, Is.EqualTo(2), "the reload belongs to the episode the round end opened");
        Assert.That(log.StartTime, Is.EqualTo(12f), "the round really starts when the new scene loads");
    }

    [Test]
    public void SceneLoad_WithoutARoundEnd_OpensANewEpisode()
    {
        EpisodeLog log = Started();
        log.RecordShot("NPC", hit: true, damage: 50f);

        log.SceneLoaded(5f); // e.g. a manual restart mid-round

        Assert.That(log.Index, Is.EqualTo(2));
        Assert.That(log.SidesJson(), Is.EqualTo("\"sides\":{}"));
    }

    [Test]
    public void SecondSceneLoad_OpensANewEpisode()
    {
        EpisodeLog log = Started();
        log.EndRound(10f);
        log.SceneLoaded(12f); // consumes the round end
        log.SceneLoaded(20f);

        Assert.That(log.Index, Is.EqualTo(3));
    }

    [Test]
    public void RoundEndAfterAReload_StillOpensAnEpisode()
    {
        EpisodeLog log = Started();
        log.SceneLoaded(5f);
        log.EndRound(10f);

        Assert.That(log.Index, Is.EqualTo(3));
    }
}
