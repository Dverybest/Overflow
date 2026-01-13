using System.Security.Claims;
using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestionService.Data;
using QuestionService.DTOs;
using QuestionService.Models;
using QuestionService.Services;
using Wolverine;

namespace QuestionService.Controllers;

[ApiController]
[Route("[controller]")]
public class QuestionsController(QuestionDbContext dbContext,IMessageBus bus,TagService tagService):ControllerBase
{
    [Authorize]
    [HttpPost]
    public async Task<ActionResult<Question>> CreateQuestionsAsync(CreateQuestionDto dto)
    {
        if (!await tagService.AreTagsValidAsync(dto.Tags))
        {
            return BadRequest($"Invalid tags.");
        }
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var name = User.FindFirstValue("name");

        if (userId is null || name is null)
        {
            return BadRequest("Cannot get user details");
        }

        var question = new Question
        {
            Title = dto.Title,
            Content = dto.Content,
            TagSlugs =  dto.Tags,
            AskerId = userId,
            AskerDisplayName = name
        };
        
        dbContext.Questions.Add(question);
        await dbContext.SaveChangesAsync();
        
        await bus.PublishAsync(new QuestionCreated(question.Id, question.Title, question.Content,
            question.CreatedAt, question.TagSlugs));
        
        return Created($"/questions/{question.Id}",question);
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Question>>> GetQuestionsAsync(string? tag)
    {
        var query = dbContext.Questions.AsQueryable();

        if (!string.IsNullOrWhiteSpace(tag))
        {
            query = query.Where(x=>x.TagSlugs.Contains(tag));
        }
        
        return await query.OrderByDescending(x=>x.CreatedAt).ToListAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Question>> GetQuestionByIdAsync(string id)
    {
        var question = await dbContext.Questions
            .Include(x=>x.Answers)
            .FirstOrDefaultAsync(x=>x.Id==id);
        
        if (question is null)
        {
            return NotFound();
        }

        await dbContext.Questions.Where(x => x.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ViewCount, x => x.ViewCount + 1));
        
        return question;
    }

    [Authorize]
    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateQuestionAsync(string id, CreateQuestionDto dto)
    {
        var question = await dbContext.Questions.FindAsync(id);
        if (question is null)
        {
            return NotFound();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId != question.AskerId)
        {
            return Forbid();
        }
        
        if (!await tagService.AreTagsValidAsync(dto.Tags))
        {
            return BadRequest($"Invalid tags.");
        }

        question.Title = dto.Title;
        question.Content = dto.Content;
        question.TagSlugs = dto.Tags;
        question.UpdatedAt = DateTime.UtcNow;
        
        await dbContext.SaveChangesAsync();
        
        await bus.PublishAsync(new QuestionUpdated(question.Id, question.Title, question.Content,
             question.TagSlugs.ToArray()));
        
        return NoContent();
    }
    
    [Authorize]
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteQuestionAsync(string id)
    {
        var question = await dbContext.Questions.FindAsync(id);
        if (question is null)
        {
            return NotFound();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId != question.AskerId)
        {
            return Forbid();
        }
        
        dbContext.Questions.Remove(question);
        await dbContext.SaveChangesAsync();
        
        await bus.PublishAsync(new QuestionDeleted(question.Id));

        return NoContent();
    }
    
    [Authorize]
    [HttpPost("{questionId}/answers")]
    public async Task<ActionResult> PostAnswer(string questionId, CreateAnswerDto dto)
    {
        var question = await dbContext.Questions.FindAsync(questionId);
        
        if (question is null) return NotFound();
        
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var name = User.FindFirstValue("name");
        
        if (userId is null || name is null) return BadRequest("Cannot get user details");

        var answer = new Answer
        {
            Content = dto.Content,
            UserId = userId,
            QuestionId = questionId
        };
        
        question.Answers.Add(answer);
        question.AnswerCount++;
        
        await dbContext.SaveChangesAsync();
        
        await bus.PublishAsync(new AnswerCountUpdated(questionId, question.AnswerCount));
        
        return Created($"/questions/{questionId}", answer);
    }
    
    [Authorize]
    [HttpPut("{questionId}/answers/{answerId}")]
    public async Task<ActionResult> UpdateAnswer(string questionId, string answerId, CreateAnswerDto dto)
    {
        var answer = await dbContext.Answers.FindAsync(answerId);
        if (answer is null) return NotFound();
        if (answer.QuestionId != questionId) return BadRequest("Cannot update answer details");
        
        answer.Content = dto.Content;
        answer.UpdatedAt = DateTime.UtcNow;
        
        await dbContext.SaveChangesAsync();
        
        return NoContent();
    }
    
    [Authorize]
    [HttpDelete("{questionId}/answers/{answerId}")]
    public async Task<ActionResult> DeleteAnswer(string questionId, string answerId)
    {
        var answer = await dbContext.Answers.FindAsync(answerId);
        var question = await dbContext.Questions.FindAsync(questionId);
        if (answer is null || question is null) return NotFound();
        if (answer.QuestionId != questionId || answer.Accepted) return BadRequest("Cannot delete this answer");
        
        dbContext.Answers.Remove(answer);
        question.AnswerCount--;
        
        await dbContext.SaveChangesAsync();
        
        await bus.PublishAsync(new AnswerCountUpdated(questionId, question.AnswerCount));
        
        return NoContent();
    }
    
    [Authorize]
    [HttpPost("{questionId}/answers/{answerId}/accept")]
    public async Task<ActionResult> AcceptAnswer(string questionId, string answerId)
    {
        var answer = await dbContext.Answers.FindAsync(answerId);
        var question = await dbContext.Questions.FindAsync(questionId);
        if (answer is null || question is null) return NotFound();
        if (answer.QuestionId != questionId || question.HasAcceptedAnswer) 
            return BadRequest("Cannot accept answer");

        answer.Accepted = true;
        question.HasAcceptedAnswer = true;
        
        await dbContext.SaveChangesAsync();
        
        await bus.PublishAsync(new AnswerAccepted(questionId));
        
        return NoContent();
    }
}