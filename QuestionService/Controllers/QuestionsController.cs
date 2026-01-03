using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestionService.Data;
using QuestionService.DTOs;
using QuestionService.Models;

namespace QuestionService.Controllers;

[ApiController]
[Route("[controller]")]
public class QuestionsController(QuestionDbContext dbContext):ControllerBase
{
    [Authorize]
    [HttpPost]
    public async Task<ActionResult<Question>> CreateQuestionsAsync(CreateQuestionDto dto)
    {
        var validTags = await dbContext.Tags.Where(x=>dto.Tags.Contains(x.Slug)).ToListAsync();
        
        var missingTags = dto.Tags.Except(validTags.Select(x=>x.Slug)).ToList();

        if (missingTags.Count > 0)
        {
            return BadRequest($"Invalid tags: {string.Join(",", missingTags)}");
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
        var question = await dbContext.Questions.FindAsync(id);
        
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
        
        var validTags = await dbContext.Tags.Where(x=>dto.Tags.Contains(x.Slug)).ToListAsync();
        var missingTags = dto.Tags.Except(validTags.Select(x=>x.Slug)).ToList();
        if (missingTags.Count > 0)
        {
            return BadRequest($"Invalid tags: {string.Join(",", missingTags)}");
        }

        question.Title = dto.Title;
        question.Content = dto.Content;
        question.TagSlugs = dto.Tags;
        question.UpdatedAt = DateTime.UtcNow;
        
        await dbContext.SaveChangesAsync();
        
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
        
        return NoContent();
    }
}