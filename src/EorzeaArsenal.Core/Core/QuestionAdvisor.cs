using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>
/// What an open question is, in the one sentence somebody needs before the buttons mean anything.
/// </summary>
public enum QuestionVerdict
{
    /// <summary>
    /// The server names a stored row and vouches for it: at least 90 % and at least 20 clear of the next
    /// best. Confirming is the ordinary answer, and the card may say so outright.
    /// </summary>
    ConfidentLink,

    /// <summary>
    /// The server names a stored row without vouching for it. The suggestion stands, the certainty does
    /// not, and a card that hides the difference is putting its own confidence on somebody else's guess.
    /// </summary>
    UncertainLink,

    /// <summary>
    /// The server proposes that this really is a new set. There are still candidates, or there would be no
    /// question at all, but the mapping had none of them left for this one.
    /// </summary>
    ProposesNew,

    /// <summary>
    /// No proposal came with the question. Nothing is preselected and nothing is recommended: an older
    /// server, or a case the mapping had no answer for.
    /// </summary>
    NoProposal,
}

/// <summary>
/// Reads a question the way the card has to say it. Separate from the window for the same reason the
/// inventory advice is: the sentence in front of a decision is a rule, and a rule belongs somewhere it can
/// be tested.
/// </summary>
/// <remarks>
/// The window drew the evidence and left the reader to work out what it meant. On an inventory row that was
/// fixed by saying what the row is and what to do about it; the question card kept the comparison, the
/// percentages and four buttons, and never got the two sentences. Somebody opening it for the first time
/// could see everything and still not know which button was theirs.
/// </remarks>
public static class QuestionAdvisor
{
    /// <summary>What this question is.</summary>
    /// <param name="held">The gearset waiting for an answer.</param>
    /// <returns>The verdict the card turns into a sentence.</returns>
    public static QuestionVerdict VerdictOf(HeldGearset held) => held.Proposal?.Action switch
    {
        ReviewAction.Link when held.Proposal.Confident => QuestionVerdict.ConfidentLink,
        ReviewAction.Link => QuestionVerdict.UncertainLink,
        ReviewAction.New => QuestionVerdict.ProposesNew,
        _ => QuestionVerdict.NoProposal,
    };

    /// <summary>
    /// Whether the card may name one button as the ordinary answer, or has to leave the choice open.
    /// </summary>
    /// <param name="held">The question.</param>
    /// <returns><see langword="true"/> only where the server vouched for its own suggestion.</returns>
    /// <remarks>
    /// Only on a confident link. Everywhere else the honest card names both ways and the question that
    /// decides between them, which is the same rule the inventory card follows: a recommendation is worth
    /// having exactly where it cannot be wrong.
    /// </remarks>
    public static bool MayRecommend(HeldGearset held) => VerdictOf(held) == QuestionVerdict.ConfidentLink;

    /// <summary>
    /// Whether this question may ride along in a one-click bulk accept, or has to be looked at.
    /// </summary>
    /// <param name="held">The question.</param>
    /// <returns><see langword="true"/> only where the server vouched for its own proposal.</returns>
    /// <remarks>
    /// <para>
    /// Two ways to qualify, and the second one matters more than it looks. <b>A vouched-for proposal</b>
    /// may be applied without looking, which is the same line the rest of this window follows: without it
    /// the card told the reader a proposal was not backed while a shortcut two lines higher offered to
    /// accept twenty of them in one press.
    /// </para>
    /// <para>
    /// <b>And a question with a single candidate</b>, whatever the score. There is nothing to decide there:
    /// one unclaimed compatible row exists and the only alternative is "this is a new set", so no choice is
    /// being hidden by the shortcut. Keeping these out looked cautious and was the opposite. The case the
    /// bulk door exists for is somebody who maintained sets on the website, left them alone for a year and
    /// now installs the plugin; their gear has drifted so far that <i>every</i> score is low. A rule that
    /// reads a low score as doubt would hand exactly that person the wall of one-by-one clicks the shortcut
    /// was built to spare them, and the low score there is not evidence against the pairing, it is the
    /// expected consequence of the year.
    /// </para>
    /// <para>
    /// What stays out is the combination: <b>several candidates and no vouching</b>. There a choice existed,
    /// the server could not settle it, and one line in a list cannot show the alternatives. That is the only
    /// case where the shortcut would decide something the reader cannot see.
    /// </para>
    /// </remarks>
    public static bool SafeForBulk(HeldGearset held) =>
        held.Proposal?.Confident == true || held.Candidates.Count <= 1;
}
