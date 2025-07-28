# Expert Coordination Guidelines

## Daily Workflow

### Morning Standup (15 minutes)
**Time**: [SET TIME - suggest 9:00 AM]
**Participants**: All experts + project lead

**Format**:
1. **Yesterday**: What did I complete?
2. **Today**: What am I working on?
3. **Blockers**: What's preventing progress?
4. **Handoffs**: What do I need from others?

### Task Check-in Process
**When starting a task**:
1. Update status to "🟡 In Progress" in task tracking
2. Notify team in daily channel
3. Check dependencies are complete

**When completing a task**:
1. Update status to "✅ Complete" 
2. Create PR for code review
3. Notify next person in chain

## Expert Responsibilities

### 🔧 Lucene Expert
**Primary Focus**: Search optimization, native features, performance
**Key Tasks**:
- SynonymFilter implementation
- Query parser improvements
- DocValues optimization
- Faceting implementation
- Performance tuning

**Daily Updates**: Focus on search quality metrics

### 🤖 AI-UX Expert  
**Primary Focus**: AI agent workflows, usability, token optimization  
**Key Tasks**:
- Response format optimization
- Context auto-loading
- Progressive disclosure
- Unified interface design
- Quality validation

**Daily Updates**: Focus on AI adoption metrics

### 👥 Collaboration Tasks
**Joint Ownership**: Features requiring both perspectives
**Process**:
1. Design session together (30 min)
2. Split implementation work
3. Joint testing and validation
4. Shared sign-off required

## Code Review Process

### Review Assignments
- **🔧 Lucene tasks**: Lucene expert reviews + 1 other
- **🤖 AI-UX tasks**: AI-UX expert reviews + 1 other  
- **👥 Joint tasks**: Both experts must approve
- **💻 General dev**: Standard review process

### Review Criteria
- [ ] Functionality works as specified
- [ ] Performance meets targets
- [ ] Tests are comprehensive
- [ ] Documentation is complete
- [ ] Integration points validated

## Conflict Resolution

### Technical Disagreements
1. **15-minute discussion** between experts
2. **Escalate to project lead** if unresolved
3. **Decision made within 24 hours**
4. **Document decision** and rationale

### Scope Creep
1. **Flag immediately** in daily standup
2. **Assess impact** on timeline/resources
3. **Project lead decides** proceed/defer
4. **Update documentation** if approved

## Communication Channels

### Primary Channel: [Setup Required]
- **Daily updates**: Brief progress notes
- **Questions**: Technical clarifications
- **Blockers**: Immediate attention needed

### Code Reviews: GitHub
- **Label PRs**: lucene-expert, ai-ux-expert, both-experts
- **Response time**: 24 hours for reviews
- **Approval required**: From designated expert

### Documentation: This Directory
- **Task updates**: Update markdown files
- **Decisions**: Document in decision log
- **Issues**: Create GitHub issues with labels

## Success Metrics Dashboard

### Daily Tracking
- [ ] Tasks completed on schedule
- [ ] Blockers identified and resolved
- [ ] Quality gates passed

### Weekly Review
- [ ] Phase goals on track
- [ ] Expert workload balanced
- [ ] Integration points successful
- [ ] Metrics improving

### Phase Completion
- [ ] All expert sign-offs received
- [ ] Performance targets met
- [ ] Ready for next phase

---
**Next Steps**:
1. Fill in expert names
2. Set up communication channels  
3. Schedule first standup
4. Begin Phase 1 Task 1.1