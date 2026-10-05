using LabOps.App.ViewModels;

namespace LabOps.Tests.App;

/// <summary>Claude's questions in the chat: answered with a button, or with what is typed in the box below.</summary>
public sealed class ChatQuestionTests
{
    [Fact]
    public async Task A_typed_answer_from_the_box_below_answers_the_question_once()
    {
        var question = new QuestionItem("Which plate layout?", ["One plate", "Two plates"]);
        question.Hint.ShouldBe("Choose an answer, or type your own in the box below.");

        question.AnswerWith("  ").ShouldBeFalse();
        question.IsAnswered.ShouldBeFalse();
        question.AnswerWith(" Three plates, one per site ").ShouldBeTrue();
        question.AnswerWith("One plate").ShouldBeFalse();

        question.Given.ShouldBe("Three plates, one per site");
        (await question.Response).ShouldBe("Three plates, one per site");
        question.AnswerCommand.CanExecute("One plate").ShouldBeFalse();
    }

    [Fact]
    public async Task A_button_answers_it_too()
    {
        var question = new QuestionItem("Which plate layout?", ["One plate", "Two plates"]);
        question.AnswerCommand.CanExecute("Two plates").ShouldBeTrue();
        question.AnswerCommand.Execute("Two plates");
        (await question.Response).ShouldBe("Two plates");
    }

    [Fact]
    public async Task A_question_left_unanswered_stops_waiting_when_the_conversation_ends()
    {
        var question = new QuestionItem("What is the sample type?", []);
        question.Hint.ShouldBe("Type your answer in the box below.");

        question.Abandon();
        question.IsAnswered.ShouldBeTrue();
        question.Given.ShouldBe("(no answer)");
        (await question.Response).ShouldBe("");
    }
}
