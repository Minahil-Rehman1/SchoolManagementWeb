using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SchoolManagementWeb.Models;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SchoolManagementWeb.Controllers;

public class ManagementController : Controller
{
    private readonly DatabaseHelper db;

    private static readonly string[] Kinds =
        ["Students", "Teachers", "Classes", "Assessments"];

    private static readonly Regex TextOnly =
        new(@"^[A-Za-z]+(?:[ '-][A-Za-z]+)*$", RegexOptions.Compiled);

    public ManagementController(DatabaseHelper db)
    {
        this.db = db;
    }

    public IActionResult Index(string section = "Students", string search = "", string? error = null)
    {
        section = IsKind(section) ? section : "Students";

        ViewBag.Section = section;
        ViewBag.Search = search?.Trim() ?? "";
        ViewBag.Error = error;

        try
        {
            using var c = db.GetConnection();
            c.Open();

            ViewBag.Data = Table(
                c,
                ListSql(section),
                ("@search", ViewBag.Search)
            );

            if (section == "Classes")
            {
                ViewBag.ClassStudents = Table(
                    c,
                    "SELECT class_id,roll_number,name,grade,section FROM students ORDER BY grade,roll_number"
                );
            }
        }
        catch (SqlException ex)
        {
            ViewBag.Error = DbError(ex);
            ViewBag.Data = new DataTable();
        }
        catch
        {
            ViewBag.Error = "Records could not be loaded.";
            ViewBag.Data = new DataTable();
        }

        return View();
    }

    [HttpGet]
    public IActionResult Create(string section)
    {
        section = IsKind(section) ? section : "Students";

        ViewBag.Section = section;
        ViewBag.Error = TempData["FormError"]?.ToString();

        Lookups(section);

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Create(
        string section,
        int? roll_number,
        string? name,
        string? gender,
        int? age,
        int? grade,
        string? class_section,
        int? class_id,
        int? teacher_id,
        string? subject,
        string? assessment_name,
        decimal? marks,
        decimal? total_marks,
        DateTime? assessment_date)
    {
        return Save(
            "Create",
            section,
            null,
            roll_number,
            name,
            gender,
            age,
            grade,
            class_section,
            class_id,
            teacher_id,
            subject,
            assessment_name,
            marks,
            total_marks,
            assessment_date
        );
    }

    [HttpGet]
    public IActionResult Edit(string section, int id, string? error = null)
    {
        if (!IsKind(section) || id <= 0)
            return RedirectToAction(nameof(Index));

        try
        {
            using var c = db.GetConnection();
            c.Open();

            var t = Table(
                c,
                OneSql(section),
                ("@id", id)
            );

            if (t.Rows.Count == 0)
            {
                return RedirectToAction(
                    nameof(Index),
                    new
                    {
                        section,
                        error = "That record no longer exists."
                    }
                );
            }

            ViewBag.Section = section;
            ViewBag.Error = error ?? TempData["FormError"]?.ToString();

            DataRow row = t.Rows[0];

            if (section == "Students")
            {
                ViewBag.RollNumber = row["roll_number"];
                ViewBag.Name = row["name"];
                ViewBag.Gender = row["gender"];
                ViewBag.Age = row["age"];
                ViewBag.Grade = row["grade"];
                ViewBag.ClassSection = row["section"];
                ViewBag.ClassId = row["class_id"];
            }
            else if (section == "Teachers")
            {
                ViewBag.TeacherId = row["teacher_id"];
                ViewBag.Name = row["name"];
                ViewBag.Gender = row["gender"];
                ViewBag.Age = row["age"];
                ViewBag.Subject = row["subject"];
            }
            else if (section == "Classes")
            {
                ViewBag.ClassId = row["class_id"];
                ViewBag.Grade = row["grade"];
                ViewBag.ClassSection = row["section"];
                ViewBag.TeacherId = row["teacher_id"];
            }
            else
            {
                ViewBag.AssessmentId = row["assessment_id"];
                ViewBag.RollNumber = row["roll_number"];
                ViewBag.AssessmentName = row["assessment_name"];
                ViewBag.Marks = row["marks"];
                ViewBag.TotalMarks = row["total_marks"];
                ViewBag.AssessmentDate =
                    Convert.ToDateTime(row["assessment_date"]).ToString("yyyy-MM-dd");
            }

            Lookups(section);

            return View();
        }
        catch
        {
            return RedirectToAction(
                nameof(Index),
                new
                {
                    section,
                    error = "The record could not be opened."
                }
            );
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Edit(
        string section,
        int id,
        int? roll_number,
        string? name,
        string? gender,
        int? age,
        int? grade,
        string? class_section,
        int? class_id,
        int? teacher_id,
        string? subject,
        string? assessment_name,
        decimal? marks,
        decimal? total_marks,
        DateTime? assessment_date)
    {
        return Save(
            "Edit",
            section,
            id,
            roll_number,
            name,
            gender,
            age,
            grade,
            class_section,
            class_id,
            teacher_id,
            subject,
            assessment_name,
            marks,
            total_marks,
            assessment_date
        );
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Delete(string section, int id)
    {
        if (!IsKind(section) || id <= 0)
            return RedirectToAction(nameof(Index));

        try
        {
            using var c = db.GetConnection();
            c.Open();

            if (
                section == "Students" &&
                Exists(
                    c,
                    "SELECT COUNT(*) FROM assessments WHERE roll_number=@id",
                    ("@id", id)
                )
            )
            {
                return ListError(
                    section,
                    "This student has assessments. Delete those first."
                );
            }

            if (
                section == "Teachers" &&
                Exists(
                    c,
                    "SELECT COUNT(*) FROM classes WHERE teacher_id=@id",
                    ("@id", id)
                )
            )
            {
                return ListError(
                    section,
                    "This teacher is assigned to a class. Reassign the class first."
                );
            }

            if (
                section == "Classes" &&
                Exists(
                    c,
                    "SELECT COUNT(*) FROM students WHERE class_id=@id",
                    ("@id", id)
                )
            )
            {
                return ListError(
                    section,
                    "This class has students. Move them first."
                );
            }

            var (table, key) = section switch
            {
                "Students" => ("students", "roll_number"),
                "Teachers" => ("teachers", "teacher_id"),
                "Classes" => ("classes", "class_id"),
                _ => ("assessments", "assessment_id")
            };

            Exec(
                c,
                $"DELETE FROM {table} WHERE {key}=@id",
                null,
                ("@id", id)
            );

            return RedirectToAction(
                nameof(Index),
                new { section }
            );
        }
        catch (SqlException ex)
        {
            return ListError(section, DbError(ex));
        }
    }

    [HttpGet]
    public IActionResult NextRoll(int grade)
    {
        if (grade < 1 || grade > 12)
            return BadRequest();

        try
        {
            using var c = db.GetConnection();
            c.Open();

            int start = RollStart(grade);

            int roll = Scalar(
                c,
                @"
                SELECT ISNULL(
                    (
                        SELECT MIN(numbers.n)
                        FROM
                        (
                            SELECT @start + number AS n
                            FROM master..spt_values
                            WHERE type = 'P'
                              AND number BETWEEN 0 AND 99
                        ) numbers
                        WHERE NOT EXISTS
                        (
                            SELECT 1
                            FROM students
                            WHERE roll_number = numbers.n
                        )
                    ),
                    @full
                )",
                null,
                ("@start", start),
                ("@full", start + 100)
            );

            if (roll > start + 99)
            {
                return Json(new
                {
                    roll = (int?)null
                });
            }

            return Json(new
            {
                roll
            });
        }
        catch
        {
            return StatusCode(500);
        }
    }

    private IActionResult Save(
        string action,
        string section,
        int? id,
        int? roll,
        string? name,
        string? gender,
        int? age,
        int? grade,
        string? classSection,
        int? classId,
        int? teacherId,
        string? subject,
        string? assessment,
        decimal? marks,
        decimal? total,
        DateTime? date)
    {
        section = IsKind(section) ? section : "Students";

        try
        {
            using var c = db.GetConnection();
            c.Open();

            return section switch
            {
                "Students" =>
                    SaveStudent(
                        c,
                        action,
                        id,
                        roll,
                        name,
                        gender,
                        age,
                        grade,
                        classSection,
                        classId
                    ),

                "Teachers" =>
                    SaveTeacher(
                        c,
                        action,
                        id,
                        name,
                        gender,
                        age,
                        subject
                    ),

                "Classes" =>
                    SaveClass(
                        c,
                        action,
                        id,
                        grade,
                        classSection,
                        teacherId
                    ),

                _ =>
                    SaveAssessment(
                        c,
                        action,
                        id,
                        roll,
                        assessment,
                        marks,
                        total,
                        date
                    )
            };
        }
        catch (SqlException ex)
        {
            return FormError(
                action,
                section,
                id,
                DbError(ex)
            );
        }
        catch
        {
            return FormError(
                action,
                section,
                id,
                "The record was not saved. Please check the entered values."
            );
        }
    }

    private IActionResult SaveStudent(
        SqlConnection c,
        string action,
        int? old,
        int? roll,
        string? name,
        string? gender,
        int? age,
        int? grade,
        string? sec,
        int? classId)
    {
        name = Title(name);
        gender = (gender ?? "").Trim();
        sec = (sec ?? "").Trim().ToUpperInvariant();

        if (!age.HasValue || !grade.HasValue || !classId.HasValue)
        {
            return FormError(
                action,
                "Students",
                old,
                "Please complete every required field using valid values."
            );
        }

        if (grade < 1 || grade > 12)
        {
            return FormError(
                action,
                "Students",
                old,
                "Please select a valid grade from 1 to 12."
            );
        }

        if (age <= 3 || age > 25)
        {
            return FormError(
                action,
                "Students",
                old,
                "Student age must be between 4 and 25."
            );
        }

        if (!ValidText(name) ||
            !Gender(gender) ||
            !ClassSection(sec))
        {
            return FormError(
                action,
                "Students",
                old,
                "Use a valid name, Male or Female, and section A, B, C, or D."
            );
        }

        var cls = Table(
            c,
            @"SELECT grade,
                     UPPER(LTRIM(RTRIM(section))) section
              FROM classes
              WHERE class_id=@id",
            ("@id", classId.Value)
        );

        if (
            cls.Rows.Count == 0 ||
            Convert.ToInt32(cls.Rows[0]["grade"]) != grade.Value ||
            cls.Rows[0]["section"].ToString() != sec
        )
        {
            return FormError(
                action,
                "Students",
                old,
                "The selected class must match the student's grade and section."
            );
        }

        if (!old.HasValue && !roll.HasValue)
        {
            int start = RollStart(grade.Value);

            roll = Scalar(
                c,
                @"
                SELECT ISNULL(
                    (
                        SELECT MIN(numbers.n)
                        FROM
                        (
                            SELECT @start + number AS n
                            FROM master..spt_values
                            WHERE type = 'P'
                              AND number BETWEEN 0 AND 99
                        ) numbers
                        WHERE NOT EXISTS
                        (
                            SELECT 1
                            FROM students
                            WHERE roll_number = numbers.n
                        )
                    ),
                    @full
                )",
                null,
                ("@start", start),
                ("@full", start + 100)
            );
        }

        if (!roll.HasValue)
        {
            return FormError(
                action,
                "Students",
                old,
                "A roll number could not be generated."
            );
        }

        if (!GradeRoll(roll.Value, grade.Value))
        {
            return FormError(
                action,
                "Students",
                old,
                $"Grade {grade.Value} roll numbers must be between {RollStart(grade.Value)} and {RollStart(grade.Value) + 99}."
            );
        }

        if (
            Exists(
                c,
                "SELECT COUNT(*) FROM students WHERE roll_number=@roll AND roll_number<>@old",
                ("@roll", roll.Value),
                ("@old", old ?? -1)
            )
        )
        {
            return FormError(
                action,
                "Students",
                old,
                "This roll number is already assigned."
            );
        }

        if (
            !old.HasValue &&
            Scalar(
                c,
                "SELECT COUNT(*) FROM students WHERE grade=@grade",
                null,
                ("@grade", grade.Value)
            ) >= 100
        )
        {
            return FormError(
                action,
                "Students",
                old,
                $"Grade {grade.Value} already has its maximum of 100 students."
            );
        }

        if (
            Scalar(
                c,
                "SELECT COUNT(*) FROM students WHERE class_id=@class AND roll_number<>@old",
                null,
                ("@class", classId.Value),
                ("@old", old ?? -1)
            ) >= 100
        )
        {
            return FormError(
                action,
                "Students",
                old,
                "The selected class already has its maximum of 100 students."
            );
        }

        if (
            old.HasValue &&
            old != roll &&
            !Cascade(c)
        )
        {
            return FormError(
                action,
                "Students",
                old,
                "Run the included SQL migration once before changing a roll number with linked assessments."
            );
        }

        if (old.HasValue)
        {
            Exec(
                c,
                @"UPDATE students
                  SET roll_number=@roll,
                      name=@name,
                      gender=@gender,
                      age=@age,
                      grade=@grade,
                      section=@sec,
                      class_id=@class
                  WHERE roll_number=@old",
                null,
                ("@roll", roll.Value),
                ("@name", name),
                ("@gender", gender),
                ("@age", age.Value),
                ("@grade", grade.Value),
                ("@sec", sec),
                ("@class", classId.Value),
                ("@old", old.Value)
            );
        }
        else
        {
            Exec(
                c,
                @"INSERT INTO students
                  (roll_number,name,gender,age,grade,section,class_id)
                  VALUES
                  (@roll,@name,@gender,@age,@grade,@sec,@class)",
                null,
                ("@roll", roll.Value),
                ("@name", name),
                ("@gender", gender),
                ("@age", age.Value),
                ("@grade", grade.Value),
                ("@sec", sec),
                ("@class", classId.Value)
            );
        }

        return RedirectToAction(
            nameof(Index),
            new { section = "Students" }
        );
    }

    private IActionResult SaveTeacher(
        SqlConnection c,
        string action,
        int? id,
        string? name,
        string? gender,
        int? age,
        string? subject)
    {
        name = Title(name);
        subject = Title(subject);
        gender = (gender ?? "").Trim();

        if (
            !ValidText(name) ||
            !ValidText(subject) ||
            !Gender(gender) ||
            !age.HasValue ||
            age <= 20 ||
            age > 70
        )
        {
            return FormError(
                action,
                "Teachers",
                id,
                "Use letters-only name and subject, Male or Female, and an age from 21 to 70."
            );
        }

        if (id.HasValue)
        {
            Exec(
                c,
                @"UPDATE teachers
                  SET name=@name,
                      gender=@gender,
                      age=@age,
                      subject=@subject
                  WHERE teacher_id=@id",
                null,
                ("@name", name),
                ("@gender", gender),
                ("@age", age.Value),
                ("@subject", subject),
                ("@id", id.Value)
            );
        }
        else
        {
            using var tx =
                c.BeginTransaction(IsolationLevel.Serializable);

            var teacherId = Scalar(
                c,
                @"SELECT
                    CASE
                        WHEN ISNULL(MAX(teacher_id),5000) < 5001
                        THEN 5001
                        ELSE MAX(teacher_id)+1
                    END
                  FROM teachers WITH (UPDLOCK,HOLDLOCK)",
                tx
            );

            Exec(
                c,
                @"INSERT INTO teachers
                  (teacher_id,name,gender,age,subject)
                  VALUES
                  (@id,@name,@gender,@age,@subject)",
                tx,
                ("@id", teacherId),
                ("@name", name),
                ("@gender", gender),
                ("@age", age.Value),
                ("@subject", subject)
            );

            tx.Commit();
        }

        return RedirectToAction(
            nameof(Index),
            new { section = "Teachers" }
        );
    }

    private IActionResult SaveClass(
        SqlConnection c,
        string action,
        int? id,
        int? grade,
        string? sec,
        int? teacher)
    {
        sec = (sec ?? "").Trim().ToUpperInvariant();

        if (
            !grade.HasValue ||
            grade < 1 ||
            grade > 12 ||
            !ClassSection(sec) ||
            !teacher.HasValue ||
            !Exists(
                c,
                "SELECT COUNT(*) FROM teachers WHERE teacher_id=@id",
                ("@id", teacher.Value)
            )
        )
        {
            return FormError(
                action,
                "Classes",
                id,
                "Choose grade 1–12, section A–D, and an existing teacher."
            );
        }

        if (
            Exists(
                c,
                @"SELECT COUNT(*)
                  FROM classes
                  WHERE grade=@grade
                    AND UPPER(LTRIM(RTRIM(section)))=@sec
                    AND class_id<>@id",
                ("@grade", grade.Value),
                ("@sec", sec),
                ("@id", id ?? -1)
            )
        )
        {
            return FormError(
                action,
                "Classes",
                id,
                "This grade and section already exists."
            );
        }

        if (
            id.HasValue &&
            Exists(
                c,
                @"SELECT COUNT(*)
                  FROM students
                  WHERE class_id=@id
                    AND grade<>@grade",
                ("@id", id.Value),
                ("@grade", grade.Value)
            )
        )
        {
            return FormError(
                action,
                "Classes",
                id,
                "This class has students. Move them before changing the grade."
            );
        }

        if (id.HasValue)
        {
            Exec(
                c,
                @"UPDATE classes
                  SET grade=@grade,
                      section=@sec,
                      teacher_id=@teacher
                  WHERE class_id=@id",
                null,
                ("@grade", grade.Value),
                ("@sec", sec),
                ("@teacher", teacher.Value),
                ("@id", id.Value)
            );
        }
        else
        {
            using var tx =
                c.BeginTransaction(IsolationLevel.Serializable);

            var classId = Scalar(
                c,
                @"SELECT ISNULL(MAX(class_id),0)+1
                  FROM classes WITH (UPDLOCK,HOLDLOCK)",
                tx
            );

            Exec(
                c,
                @"INSERT INTO classes
                  (class_id,grade,section,teacher_id)
                  VALUES
                  (@id,@grade,@sec,@teacher)",
                tx,
                ("@id", classId),
                ("@grade", grade.Value),
                ("@sec", sec),
                ("@teacher", teacher.Value)
            );

            tx.Commit();
        }

        return RedirectToAction(
            nameof(Index),
            new { section = "Classes" }
        );
    }

    private IActionResult SaveAssessment(
        SqlConnection c,
        string action,
        int? id,
        int? roll,
        string? assessment,
        decimal? marks,
        decimal? total,
        DateTime? date)
    {
        assessment = Title(assessment);

        if (
            !roll.HasValue ||
            !Exists(
                c,
                "SELECT COUNT(*) FROM students WHERE roll_number=@id",
                ("@id", roll.Value)
            ) ||
            !ValidText(assessment) ||
            !marks.HasValue ||
            marks < 0 ||
            !total.HasValue ||
            total <= 0 ||
            marks > total ||
            !date.HasValue ||
            date.Value.Date > DateTime.Today
        )
        {
            return FormError(
                action,
                "Assessments",
                id,
                "Select a student; assessment name needs letters only; marks must be between 0 and total; date cannot be in the future."
            );
        }

        if (
            Exists(
                c,
                @"SELECT COUNT(*)
                  FROM assessments
                  WHERE roll_number=@roll
                    AND assessment_name=@name
                    AND assessment_date=@date
                    AND assessment_id<>@id",
                ("@roll", roll.Value),
                ("@name", assessment),
                ("@date", date.Value.Date),
                ("@id", id ?? -1)
            )
        )
        {
            return FormError(
                action,
                "Assessments",
                id,
                "This assessment already exists for this student on this date."
            );
        }

        if (id.HasValue)
        {
            Exec(
                c,
                @"UPDATE assessments
                  SET roll_number=@roll,
                      assessment_name=@name,
                      marks=@marks,
                      total_marks=@total,
                      assessment_date=@date
                  WHERE assessment_id=@id",
                null,
                ("@roll", roll.Value),
                ("@name", assessment),
                ("@marks", marks.Value),
                ("@total", total.Value),
                ("@date", date.Value.Date),
                ("@id", id.Value)
            );
        }
        else
        {
            Exec(
                c,
                @"INSERT INTO assessments
                  (roll_number,assessment_name,marks,total_marks,assessment_date)
                  VALUES
                  (@roll,@name,@marks,@total,@date)",
                null,
                ("@roll", roll.Value),
                ("@name", assessment),
                ("@marks", marks.Value),
                ("@total", total.Value),
                ("@date", date.Value.Date)
            );
        }

        return RedirectToAction(
            nameof(Index),
            new { section = "Assessments" }
        );
    }

    private void Lookups(string section)
    {
        try
        {
            using var c = db.GetConnection();
            c.Open();

            if (section == "Students")
            {
                ViewBag.Classes = Table(
                    c,
                    @"SELECT class_id,
                             grade,
                             UPPER(LTRIM(RTRIM(section))) section
                      FROM classes
                      ORDER BY grade,section"
                );
            }

            if (section == "Classes")
            {
                ViewBag.Teachers = Table(
                    c,
                    @"SELECT teacher_id,name
                      FROM teachers
                      ORDER BY teacher_id"
                );
            }

            if (section == "Assessments")
            {
                ViewBag.Students = Table(
                    c,
                    @"SELECT roll_number,name,grade
                      FROM students
                      ORDER BY grade,roll_number"
                );
            }
        }
        catch
        {
            ViewBag.Error = "Required choices could not be loaded.";
        }
    }

    private static string ListSql(string s)
    {
        return s switch
        {
            "Students" =>
                @"SELECT
                    s.roll_number,
                    s.name,
                    s.gender,
                    s.age,
                    s.grade,
                    s.section,
                    s.class_id,
                    CONCAT(
                        'Grade ',
                        c.grade,
                        ' - ',
                        UPPER(c.section)
                    ) class_name
                  FROM students s
                  LEFT JOIN classes c
                    ON c.class_id=s.class_id
                  WHERE
                    @search=''
                    OR CAST(s.roll_number AS varchar) LIKE '%'+@search+'%'
                    OR s.name LIKE '%'+@search+'%'
                    OR CAST(s.grade AS varchar) LIKE '%'+@search+'%'
                  ORDER BY s.grade,s.roll_number",

            "Teachers" =>
                @"SELECT
                    teacher_id,
                    name,
                    gender,
                    age,
                    subject
                  FROM teachers
                  WHERE
                    @search=''
                    OR CAST(teacher_id AS varchar) LIKE '%'+@search+'%'
                    OR name LIKE '%'+@search+'%'
                    OR subject LIKE '%'+@search+'%'
                  ORDER BY teacher_id",

            "Classes" =>
                @"SELECT
                    c.class_id,
                    c.grade,
                    UPPER(c.section) section,
                    c.teacher_id,
                    t.name teacher_name,
                    COUNT(s.roll_number) student_strength
                  FROM classes c
                  LEFT JOIN teachers t
                    ON t.teacher_id=c.teacher_id
                  LEFT JOIN students s
                    ON s.class_id=c.class_id
                  WHERE
                    @search=''
                    OR CAST(c.grade AS varchar) LIKE '%'+@search+'%'
                    OR c.section LIKE '%'+@search+'%'
                    OR t.name LIKE '%'+@search+'%'
                  GROUP BY
                    c.class_id,
                    c.grade,
                    c.section,
                    c.teacher_id,
                    t.name
                  ORDER BY c.grade,c.section",

            _ =>
                @"SELECT
                    a.assessment_id,
                    a.roll_number,
                    s.name student_name,
                    a.assessment_name,
                    a.marks,
                    a.total_marks,
                    a.assessment_date
                  FROM assessments a
                  LEFT JOIN students s
                    ON s.roll_number=a.roll_number
                  WHERE
                    @search=''
                    OR CAST(a.assessment_id AS varchar) LIKE '%'+@search+'%'
                    OR s.name LIKE '%'+@search+'%'
                    OR a.assessment_name LIKE '%'+@search+'%'
                  ORDER BY a.assessment_id"
        };
    }

    private static string OneSql(string s)
    {
        return s switch
        {
            "Students" =>
                @"SELECT
                    roll_number,
                    name,
                    gender,
                    age,
                    grade,
                    section,
                    class_id
                  FROM students
                  WHERE roll_number=@id",

            "Teachers" =>
                @"SELECT
                    teacher_id,
                    name,
                    gender,
                    age,
                    subject
                  FROM teachers
                  WHERE teacher_id=@id",

            "Classes" =>
                @"SELECT
                    class_id,
                    grade,
                    section,
                    teacher_id
                  FROM classes
                  WHERE class_id=@id",

            _ =>
                @"SELECT
                    assessment_id,
                    roll_number,
                    assessment_name,
                    marks,
                    total_marks,
                    assessment_date
                  FROM assessments
                  WHERE assessment_id=@id"
        };
    }

    private IActionResult FormError(
        string action,
        string section,
        int? id,
        string error)
    {
        TempData["FormError"] = error;

        if (id.HasValue)
        {
            return RedirectToAction(
                action,
                new
                {
                    section,
                    id
                }
            );
        }

        return RedirectToAction(
            action,
            new
            {
                section
            }
        );
    }

    private IActionResult ListError(
        string section,
        string error)
    {
        return RedirectToAction(
            nameof(Index),
            new
            {
                section,
                error
            }
        );
    }

    private static bool IsKind(string s)
    {
        return Kinds.Contains(s);
    }

    private static bool ValidText(string? s)
    {
        return
            !string.IsNullOrWhiteSpace(s) &&
            s.Length <= 100 &&
            TextOnly.IsMatch(s);
    }

    private static bool Gender(string s)
    {
        return s is "Male" or "Female";
    }

    private static bool ClassSection(string s)
    {
        return s is "A" or "B" or "C" or "D";
    }

    private static string Title(string? s)
    {
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
            (s ?? "").Trim().ToLower()
        );
    }

    private static int RollStart(int grade)
    {
        return grade * 100;
    }

    private static bool GradeRoll(int roll, int grade)
    {
        return
            grade is >= 1 and <= 12 &&
            roll >= RollStart(grade) &&
            roll <= RollStart(grade) + 99;
    }

    private static DataTable Table(
        SqlConnection c,
        string sql,
        params (string, object)[] p)
    {
        using var cmd = new SqlCommand(sql, c);

        foreach (var x in p)
        {
            cmd.Parameters.AddWithValue(
                x.Item1,
                x.Item2 ?? DBNull.Value
            );
        }

        using var a = new SqlDataAdapter(cmd);

        var t = new DataTable();

        a.Fill(t);

        return t;
    }

    private static int Scalar(
        SqlConnection c,
        string sql,
        SqlTransaction? tx = null,
        params (string, object)[] p)
    {
        using var cmd = new SqlCommand(sql, c, tx);

        foreach (var x in p)
        {
            cmd.Parameters.AddWithValue(
                x.Item1,
                x.Item2
            );
        }

        return Convert.ToInt32(
            cmd.ExecuteScalar()
        );
    }

    private static bool Exists(
        SqlConnection c,
        string sql,
        params (string, object)[] p)
    {
        return Scalar(c, sql, null, p) > 0;
    }

    private static void Exec(
        SqlConnection c,
        string sql,
        SqlTransaction? tx,
        params (string, object)[] p)
    {
        using var cmd = new SqlCommand(sql, c, tx);

        foreach (var x in p)
        {
            cmd.Parameters.AddWithValue(
                x.Item1,
                x.Item2
            );
        }

        cmd.ExecuteNonQuery();
    }

   private static bool Cascade(SqlConnection c)
{
    return Scalar(
        c,
        @"SELECT COUNT(*)
          FROM sys.foreign_keys fk
          INNER JOIN sys.tables parent_table
              ON fk.parent_object_id = parent_table.object_id
          INNER JOIN sys.tables referenced_table
              ON fk.referenced_object_id = referenced_table.object_id
          WHERE parent_table.name = 'assessments'
            AND referenced_table.name = 'students'
            AND fk.update_referential_action = 1"
    ) > 0;
}

    private static string DbError(SqlException e)
    {
        return e.Number switch
        {
            2627 or 2601 =>
                "That value is already in use.",

            547 =>
                "This change conflicts with a linked record.",

            _ =>
                "Database error: " + e.Message
        };
    }
}