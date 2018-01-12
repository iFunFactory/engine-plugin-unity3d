from django.contrib import admin
from board.models import announcement

# Display list
class announcementAdmin(admin.ModelAdmin):
    list_display = ('subject', 'create_date')

# Register your models here.
admin.site.register(announcement, announcementAdmin)
