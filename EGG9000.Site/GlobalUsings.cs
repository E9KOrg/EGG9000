// ApplicationUser (the Identity user, defined in EGG9000.Common) is referenced across the Site's
// controllers and scaffolded Identity pages. Surface its namespace globally so each file does not
// need an explicit import after the IdentityUser -> ApplicationUser switch.
global using EGG9000.Common.Database.Entities;
